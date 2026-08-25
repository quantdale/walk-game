# ADR 0008 — Repository Identity and Single-Writer Concurrency Guards

## Status

Accepted

## Context

Walk Game (`quantdale/walk-game`) shares a product concept with an independent
sibling repository (`quantdale/simple-walk-game`). Multiple autonomous agent
sessions have operated on this codebase, creating two concrete risk classes:

1. **Wrong-repository execution** — a session carrying assumptions, prompts,
   SHAs, or state from the sibling could mutate this checkout (or vice versa)
   silently, because nothing proved repository identity before editing.
2. **Concurrent-writer lost updates** — two mutable sessions in one worktree or
   racing against the same remote branch can overwrite each other's work;
   without explicit detection the "natural" recovery is a force push, which
   destroys the competing session's output.

Documentation-only rules cannot enforce either constraint: agents may skip
reading them, and no runtime signal fails when they are violated.

## Decision

1. **Machine-readable identity.** `.repo-identity.json` records
   `schemaVersion`, `repository: quantdale/walk-game`, and `project`.
2. **Fail-closed identity guard**, in both PowerShell
   (`scripts/Assert-RepoIdentity.ps1`) and POSIX sh
   (`scripts/assert-repo-identity.sh`), validating: git root, identity file,
   normalized HTTPS/SSH `origin`, repository fingerprints (domain sources,
   standalone test project, status doc, editor pin), and `GITHUB_REPOSITORY`
   when running under CI. Any mismatch terminates non-zero before further work.
3. **Single-writer lease** (`scripts/writer-lock.sh` / `WriterLock.ps1`): an
   atomic directory-create lock under untracked `.git/` capturing repo slug,
   session id, hostname, PID, branch, start SHA, and timestamp. Second writers
   fail immediately; stale locks are never stolen silently — deliberate
   operator `--force` is recorded in the replacement lease.
4. **Lost-update protection** (`scripts/check-remote-advance.sh` /
   `Check-RemoteAdvance.ps1`, plus the pre-push hook): fetch and prove the
   remote branch is contained in local HEAD; otherwise stop automatic
   integration. Remote ref deletion is refused outright from hooks. Force
   pushes require explicit human authorization.
5. **Tracked hooks** under `.githooks/` (`pre-commit`, `pre-push`) activated per
   clone via `git config core.hooksPath .githooks`
   (`scripts/setup-hooks.ps1|.sh`); CI re-checks identity independently of local
   opt-in, including a hard `GITHUB_REPOSITORY` equality gate.
6. **Deterministic self-verification**: `scripts/Test-AgentGuards.ps1` proves
   both implementations across twelve scenarios (identity pass/fail matrix,
   nested invocation, HTTPS/SSH normalization, concurrent acquire refusal,
   release/re-acquire, stale-lock refusal, race detection, hook-level force/
   deletion refusal, sandbox containment) entirely against local fixture
   repositories with git transports restricted to `file`.

AGENTS.md carries the resulting operational contract: identity-first workflow,
sibling isolation rules, one-writer-one-branch-one-worktree ownership, session
lock usage, start-SHA/remote-advancement discipline, destructive-operation
prohibitions, and incident-recovery steps. Harness goal adapters
(`.agents/skills/goal`, `.claude`, `.opencode`, `.kimi-code`) now route through
the identity guard as their first step.

## Consequences

- Every mutation path — human or autonomous — has a cheap, scriptable proof of
  "which repository is this" and "am I the only writer".
- A collision event becomes loud and recoverable instead of silent and
  destructive; reconciliation is always deliberate and followed by full
  reverification.
- Hooks are one layer only: they require per-clone opt-in, so CI and the
  deterministic suites provide enforcement that does not depend on local
  configuration. None of these guards replace review; they bound blast radius.
- The sh and PowerShell implementations must stay semantically in sync; the
  guard suite runs both matrices to make drift visible.
