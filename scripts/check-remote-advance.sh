#!/bin/sh
# Lost-update protection for quantdale/walk-game (POSIX sh twin of
# scripts/Check-RemoteAdvance.ps1).
#
# Fetches the target branch from origin and proves the remote has NOT gained
# commits unreachable from local HEAD since session start. Detects the race and
# STOPS; reconciliation is always deliberate. Never mutates the remote.
#
# Usage: check-remote-advance.sh [branch]   (default: current branch)
# Exit codes: 0 = remote safe to integrate; 1 = remote advanced unexpectedly
#             (STOP: reconcile deliberately); 2 = environment failure.

fail_env() {
    echo "REMOTE-ADVANCE GUARD: $*" >&2
    exit 2
}

command -v git >/dev/null 2>&1 || fail_env "git not found on PATH"
ROOT=$(git rev-parse --show-toplevel 2>/dev/null) || fail_env "not inside a git work tree"
cd "$ROOT" || fail_env "cannot enter repository root"

BRANCH=${1:-$(git rev-parse --abbrev-ref HEAD)}
[ -n "$BRANCH" ] && [ "$BRANCH" != "HEAD" ] || fail_env "detached HEAD; pass an explicit branch"
LOCAL=$(git rev-parse -q --verify HEAD) || fail_env "no local commit"

echo "fetching origin/$BRANCH for race check..."
git fetch --quiet origin "$BRANCH" 2>&1 || fail_env "could not fetch '$BRANCH' from origin"

REMOTE=$(git rev-parse -q --verify FETCH_HEAD 2>/dev/null)
if [ -z "$REMOTE" ]; then
    echo "remote-advance OK: origin/$BRANCH does not exist yet (new branch)"
    exit 0
fi

if git merge-base --is-ancestor "$REMOTE" "$LOCAL" 2>/dev/null; then
    echo "remote-advance OK: origin/$BRANCH ($REMOTE) is contained in local HEAD ($LOCAL)"
    exit 0
fi

echo "REMOTE-ADVANCE GUARD: origin/$BRANCH moved during this session with commits not in your HEAD. STOP automatic integration." >&2
echo "" >&2
echo "Unexpected remote-only commits:" >&2
git log --oneline --decorate "$LOCAL..$REMOTE" >&2 || true
echo "" >&2
echo "Do NOT force-push, reset --hard over, discard, or blindly overwrite this work." >&2
echo "Inspect and reconcile deliberately (merge/rebase after reading the other session's intent)," >&2
echo "then rerun all impacted verification before integrating." >&2
exit 1
