#!/bin/sh
# Repository identity guard for quantdale/walk-game. Fails closed (exit 1) on any
# mismatch. Used by .githooks/pre-commit, .githooks/pre-push, and local/CI gates.
# POSIX sh; works under Git Bash on Windows.
#
# Usage: assert-repo-identity.sh [--ci-mode]
#   --ci-mode  Require GITHUB_REPOSITORY to be set and equal to the expected slug
#              (used by CI and by the deterministic guard tests).

EXPECTED_SLUG="quantdale/walk-game"
EXPECTED_PROJECT="Walk Game"

fail() {
    echo "REPO IDENTITY GUARD: $*" >&2
    exit 1
}

CI_MODE=0
for arg in "$@"; do
    case "$arg" in
        --ci-mode) CI_MODE=1 ;;
        *) fail "unknown argument: $arg" ;;
    esac
done

command -v git >/dev/null 2>&1 || fail "git not found on PATH"

ROOT=$(git rev-parse --show-toplevel 2>/dev/null) || fail "not inside a git work tree"
cd "$ROOT" || fail "cannot enter repository root"

[ -f "$ROOT/.repo-identity.json" ] || fail ".repo-identity.json missing"

ID_SLUG=$(sed -n 's/.*"repository"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$ROOT/.repo-identity.json" | head -n 1)
[ -n "$ID_SLUG" ] || fail "identity file has no repository field"
[ "$ID_SLUG" = "$EXPECTED_SLUG" ] || fail "identity file repository '$ID_SLUG' != '$EXPECTED_SLUG' (this project is NOT quantdale/simple-walk-game)"

ID_PROJECT=$(sed -n 's/.*"project"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$ROOT/.repo-identity.json" | head -n 1)
[ -n "$ID_PROJECT" ] || fail "identity file has no project field"
[ "$ID_PROJECT" = "$EXPECTED_PROJECT" ] || fail "identity project '$ID_PROJECT' != '$EXPECTED_PROJECT'"

REMOTE_URL=$(git remote get-url origin 2>/dev/null || git config --get remote.origin.url 2>/dev/null)
[ -n "$REMOTE_URL" ] || fail "origin remote is not configured"

SLUG=""
case "$REMOTE_URL" in
    git@github.com:*/*)          SLUG=${REMOTE_URL#git@github.com:} ;;
    ssh://git@github.com/*)      SLUG=${REMOTE_URL#ssh://git@github.com/} ;;
    https://github.com/*)        SLUG=${REMOTE_URL#https://github.com/} ;;
    http://github.com/*)         SLUG=${REMOTE_URL#http://github.com/} ;;
    *) fail "origin '$REMOTE_URL' is not a recognized github.com remote for $EXPECTED_SLUG" ;;
esac
SLUG=${SLUG%.git}
[ "$SLUG" = "$EXPECTED_SLUG" ] || fail "origin '$REMOTE_URL' resolves to '$SLUG', expected '$EXPECTED_SLUG'"

for f in \
    Assets/WalkGame/App/GameHost.cs \
    verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj \
    docs/IMPLEMENTATION_STATUS.md \
    docs/MASTER_PLAN.md \
    AGENTS.md \
    ProjectSettings/ProjectVersion.txt
do
    [ -f "$ROOT/$f" ] || fail "repository fingerprint missing: $f"
done

grep -q 'm_EditorVersion:[[:space:]]*6000\.3' "$ROOT/ProjectSettings/ProjectVersion.txt" \
    || fail "editor pin fingerprint mismatch in ProjectSettings/ProjectVersion.txt"

GH_REPO=${GITHUB_REPOSITORY:-}
if [ "$CI_MODE" -eq 1 ]; then
    [ -n "$GH_REPO" ] || fail "--ci-mode: GITHUB_REPOSITORY is not set"
fi
if [ -n "$GH_REPO" ]; then
    [ "$GH_REPO" = "$EXPECTED_SLUG" ] || fail "GITHUB_REPOSITORY '$GH_REPO' != '$EXPECTED_SLUG'"
fi

echo "repo identity OK: $EXPECTED_SLUG ($EXPECTED_PROJECT)"
exit 0
