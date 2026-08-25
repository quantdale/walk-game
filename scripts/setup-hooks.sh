#!/bin/sh
# Activates the tracked repository hooks (.githooks) for this clone/worktree by
# setting core.hooksPath. Local-only config; run once per clone.
set -eu
git rev-parse --show-toplevel >/dev/null 2>&1 || {
    echo "setup-hooks: not inside a git work tree" >&2
    exit 1
}
git config core.hooksPath .githooks
echo "hooks activated: core.hooksPath=.githooks (identity + pre-push race guards for quantdale/walk-game)"
