#!/bin/sh
# Lost-update protection for quantdale/walk-game (POSIX sh twin of
# scripts/Check-RemoteAdvance.ps1).
#
# Fetches the target branch from the origin push transport and proves the remote has NOT gained
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
# M8.7 H5: an absent new branch must be positively distinguished from a
# transport/auth failure. Query the exact ref first; only a confirmed absence
# means "new branch", while a failed query fails closed.
REMOTE_URL=$(git remote get-url --push origin 2>/dev/null) || fail_env "could not determine origin push transport"
[ -n "$REMOTE_URL" ] || fail_env "origin has no usable push transport"
PROBE=$(git ls-remote --heads "$REMOTE_URL" "refs/heads/$BRANCH" 2>/dev/null)
if [ $? -ne 0 ]; then
    fail_env "could not query origin for '$BRANCH' (transport/auth)"
fi

if [ -z "$PROBE" ]; then
    echo "remote-advance OK: origin/$BRANCH does not exist yet (new branch)"
    exit 0
fi

git fetch --quiet "$REMOTE_URL" "$BRANCH" 2>&1 || fail_env "could not fetch '$BRANCH' from origin"

REMOTE=$(git rev-parse -q --verify FETCH_HEAD 2>/dev/null)
if [ -z "$REMOTE" ]; then
    # ls-remote proved the ref exists, but the fetch produced nothing: treat as
    # an environment failure rather than a missing branch.
    fail_env "origin/$BRANCH ref reported present but fetch yielded no commit"
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
