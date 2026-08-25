# .githooks

Tracked Git hooks for `quantdale/walk-game` (NOT `quantdale/simple-walk-game`).

| Hook | Enforces |
| --- | --- |
| `pre-commit` | Repository identity guard (`scripts/assert-repo-identity.sh`) — fail closed before anything is committed. |
| `pre-push` | Identity guard + no remote-branch deletion + lost-update race check (`git fetch` + ancestry proof per pushed branch; refuses pushes that would require `--force`). |

Hooks are one enforcement layer only — CI re-checks identity independently, and
the deterministic suites under `scripts/Test-AgentGuards.ps1` /
`test-agent-guards.sh` verify hook logic itself.

## One-time activation per clone/worktree

```bash
git config core.hooksPath .githooks
```

or, equivalently:

```powershell
./scripts/setup-hooks.ps1          # Windows
sh scripts/setup-hooks.sh          # anywhere with sh
```

`core.hooksPath` is intentionally untracked local config: every clone opts in.
CI performs the same checks server-side regardless of local opt-in.
