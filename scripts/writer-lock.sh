#!/bin/sh
# Repository-local single-writer lease for quantdale/walk-game.
#
# One mutable agent session per worktree. The lease lives under .git/ (never
# tracked, never committed) and captures repo slug, session id, hostname, PID,
# branch, start SHA, and acquisition time. A second acquire fails immediately;
# stale/conflicting locks are NEVER stolen silently - clearing them requires an
# explicit --force from the operator, which is recorded in the new lease.
#
# Usage: writer-lock.sh <acquire|release|status> [--force]
# Environment:
#   WRITER_LOCK_SESSION       session id (default: generated)
#   WRITER_LOCK_MAX_AGE_HOURS advisory staleness threshold (default: 24)

EXPECTED_SLUG="quantdale/walk-game"

fail() {
    echo "WRITER LOCK: $*" >&2
    exit 1
}

CMD=""
FORCE=0
for arg in "$@"; do
    case "$arg" in
        acquire|release|status) CMD=$arg ;;
        --force) FORCE=1 ;;
        *) fail "unknown argument: $arg" ;;
    esac
done
[ -n "$CMD" ] || fail "usage: writer-lock.sh <acquire|release|status> [--force]"

command -v git >/dev/null 2>&1 || fail "git not found on PATH"
ROOT=$(git rev-parse --show-toplevel 2>/dev/null) || fail "not inside a git work tree"

LOCK_DIR="$ROOT/.git/walk-game-writer.lock"
LOCK_FILE="$LOCK_DIR/lock.json"

field() {
    sed -n "s/.*\"$1\"[[:space:]]*:[[:space:]]*\"\([^\"]*\)\".*/\1/p" "$LOCK_FILE" 2>/dev/null | head -n 1
}

SESSION=${WRITER_LOCK_SESSION:-}
if [ -z "$SESSION" ]; then
    SESSION="sess-$(date -u +%Y%m%dT%H%M%SZ)-$$-${RANDOM}${RANDOM}"
fi

describe_lock() {
    echo "  holder session : $(field sessionId)"
    echo "  repository     : $(field repository)"
    echo "  hostname       : $(field hostname)"
    echo "  pid            : $(field pid)"
    echo "  branch         : $(field branch)"
    echo "  start SHA      : $(field startSha)"
    echo "  acquired (utc) : $(field acquiredUtc)"
}

staleness_report() {
    epoch=$(field acquiredEpoch)
    now=$(date -u +%s)
    if [ -n "$epoch" ] && [ "$now" -ge "$epoch" ] 2>/dev/null; then
        age_h=$(( (now - epoch) / 3600 ))
        max_h=${WRITER_LOCK_MAX_AGE_HOURS:-24}
        echo "  lock age       : ${age_h}h (advisory stale threshold: ${max_h}h)"
        if [ "$age_h" -ge "$max_h" ]; then echo "  assessment     : STALE (recovery still requires explicit --force)"; fi
    fi
    holder_host=$(field hostname)
    this_host=$(hostname 2>/dev/null || echo unknown)
    if [ -n "$holder_host" ] && [ "$holder_host" != "$this_host" ]; then
        echo "  assessment     : held by another host '$holder_host' (PID liveness unknowable here)"
    elif [ "$(uname -s 2>/dev/null)" != "MINGW_NT"* ] && [ "$(field pid)" != "" ]; then
        if ! kill -0 "$(field pid)" 2>/dev/null; then
            echo "  assessment     : holder PID $(field pid) is not alive on this host (STALE candidate)"
        fi
    fi
}

case "$CMD" in
    status)
        if [ -f "$LOCK_FILE" ]; then
            echo "active writer lock:"
            describe_lock
            staleness_report
        else
            echo "no active writer lock"
        fi
        exit 0
        ;;
esac

BRANCH=$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo unknown)
START_SHA=$(git rev-parse HEAD 2>/dev/null || fail "repository has no commits yet")
NOW_ISO=$(date -u +%Y-%m-%dT%H:%M:%SZ)
NOW_EPOCH=$(date -u +%s)
HOSTNAME_VAL=$(hostname 2>/dev/null || echo unknown)
PID_VAL=$$

write_lock() {
    override_note="$1"
    {
        printf '{\n'
        printf '  "schemaVersion": 1,\n'
        printf '  "repository": "%s",\n' "$EXPECTED_SLUG"
        printf '  "sessionId": "%s",\n' "$SESSION"
        printf '  "hostname": "%s",\n' "$HOSTNAME_VAL"
        printf '  "pid": "%s",\n' "$PID_VAL"
        printf '  "branch": "%s",\n' "$BRANCH"
        printf '  "startSha": "%s",\n' "$START_SHA"
        printf '  "acquiredUtc": "%s",\n' "$NOW_ISO"
        printf '  "acquiredEpoch": "%s"%s\n' "$NOW_EPOCH" "$override_note"
        printf '}\n'
    } >"$LOCK_FILE" || fail "cannot write $LOCK_FILE"
}

case "$CMD" in
    acquire)
        if mkdir "$LOCK_DIR" 2>/dev/null; then
            write_lock ""
            echo "writer lock acquired: session=$SESSION branch=$BRANCH startSha=$START_SHA"
            exit 0
        fi

        if [ ! -f "$LOCK_FILE" ]; then
            fail "lock directory exists but unreadable/corrupt; recover manually with --force after inspection"
        fi

        if [ "$(field repository)" != "$EXPECTED_SLUG" ]; then
            echo "WRITER LOCK: existing lock belongs to a different repository identity ('$(field repository)'); refusing." >&2
            describe_lock
            exit 2
        fi

        if [ "$(field sessionId)" = "$SESSION" ]; then
            echo "writer lock already held by this session ($SESSION)"
            exit 0
        fi

        echo "WRITER LOCK: another writer holds this worktree; acquire refused." >&2
        describe_lock
        staleness_report
        if [ "$FORCE" -eq 1 ]; then
            PREV="session=$(field sessionId) host=$(field hostname) pid=$(field pid) sha=$(field startSha) acquired=$(field acquiredUtc)"
            rm -rf "$LOCK_DIR" || fail "cannot remove conflicting lock for forced takeover"
            mkdir "$LOCK_DIR" 2>/dev/null || fail "cannot recreate lock directory after forced takeover"
            write_lock ",
  \"forcedOverride\": \"true\",
  \"previousOwner\": \"$PREV\""
            echo "writer lock FORCE-OVERIDDEN by operator: session=$SESSION (previous: $PREV)" >&2
            exit 0
        fi
        echo "If this lock is genuinely abandoned, recover deliberately:" >&2
        echo "  1. inspect: sh scripts/writer-lock.sh status" >&2
        echo "  2. confirm no other live session, then: sh scripts/writer-lock.sh acquire --force" >&2
        exit 2
        ;;

    release)
        if [ ! -f "$LOCK_FILE" ]; then
            echo "no writer lock present; nothing to release"
            exit 0
        fi
        HOLDER=$(field sessionId)
        if [ "$HOLDER" != "$SESSION" ] && [ "$FORCE" -ne 1 ]; then
            echo "WRITER LOCK: lock belongs to session '$HOLDER', not '$SESSION'; refusing (use --force only as explicit operator recovery)." >&2
            exit 2
        fi
        rm -rf "$LOCK_DIR" || fail "cannot remove lock directory"
        echo "writer lock released: session=$SESSION"
        exit 0
        ;;
esac
