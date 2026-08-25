# AGENTS.md

## REPOSITORY IDENTITY — FAIL CLOSED

This repository is **`quantdale/walk-game`**.
It is NOT **`quantdale/simple-walk-game`**.

Before modifying any file, run the repository identity guard and confirm it exits 0:

```bash
sh scripts/assert-repo-identity.sh          # POSIX / Git Bash
./scripts/Assert-RepoIdentity.ps1           # PowerShell
```

The guard validates: git work-tree root, `.repo-identity.json`, the normalized
`origin` remote (HTTPS or SSH), repository-specific fingerprints, and
`GITHUB_REPOSITORY` under CI. If identity is not exact, STOP. Do not edit, do not
commit, do not push; report the mismatch instead.

Enforcement layers (all must pass; hooks are only one layer):

1. `pre-commit` / `pre-push` hooks — activate once per clone with
   `git config core.hooksPath .githooks` (or `scripts/setup-hooks.ps1|.sh`).
2. CI (`repository-identity` job) fails when `GITHUB_REPOSITORY != quantdale/walk-game`.
3. Deterministic proof: `scripts/Test-AgentGuards.ps1`.

This repository is being developed from a documentation-first master plan. Before changing code, read the project documentation.

## Required reading order

1. `docs/MASTER_PLAN.md`
2. `docs/ROADMAP.md`
3. `docs/TECHNICAL_ARCHITECTURE.md`
4. `docs/DATA_MODEL.md`
5. The domain-specific document for the task
6. `docs/AGENT_EXECUTION_GUIDE.md`

For movement, permissions, location, analytics, or health-platform work also read:

- `docs/ACTIVITY_REWARD_SYSTEM.md`
- `docs/MOBILE_ACTIVITY_INTEGRATION.md`
- `docs/PRIVACY_SAFETY_ANTI_CHEAT.md`
- `docs/RESEARCH_NOTES.md`

For performance-sensitive work read:

- `docs/TESTING_AND_PERFORMANCE.md`

## Sibling-repository isolation

`quantdale/simple-walk-game` is an independent project with a similar product
concept. From this session:

- Never clone, fetch, push, configure remotes for, open, or mutate the sibling.
- Never transfer assumptions, campaign state, SHAs, implementation status,
  roadmap state, source files, prompts, or branch names between them.
- Never use a path, remote, branch, or SHA that originated in the sibling.
- If you find sibling references in this repo, they are permitted only inside
  the explicit protection mechanism (identity guard/tests/this contract) or as
  clearly justified documentation.

## Concurrent writers and worktree ownership

Concurrent autonomous sessions are allowed only under:

**one writer = one branch = one worktree**

- Never run two mutable agents in the same checkout.
- Branch naming per campaign/session: `agent/walk-game/<campaign>-<session-id>`.
- Before mutating anything, acquire the single-writer lease (next section).
- Read-only sessions (audits) need no lease but must not write.

## Session writer lock

Acquire before your first mutation; release on normal completion:

```bash
sh scripts/writer-lock.sh acquire     # refuses while another writer holds it
sh scripts/writer-lock.sh status      # inspect the current lease
sh scripts/writer-lock.sh release     # by the holding session id
```

(PowerShell twins: `scripts/WriterLock.ps1 acquire|release|status`.) The lease
lives untracked under `.git/` and records repo slug, session id, hostname, PID,
branch, start SHA, and timestamp. Rules:

- A second writer fails immediately; locks are never stolen silently.
- Stale/abandoned locks require deliberate operator recovery:
  inspect with `status`, then explicitly re-acquire with `--force` / `-Force`
  (the override and previous owner are recorded in the new lease).

## Start SHA and remote advancement (lost-update protection)

Record your start commit before working (the writer lock captures it
automatically). Before integration/push:

```bash
git fetch origin
sh scripts/check-remote-advance.sh    # or scripts/Check-RemoteAdvance.ps1
```

If the intended remote branch gained commits not contained in your HEAD during
your session, STOP automatic integration. Do NOT force-push, `reset --hard`
over, discard, or blindly overwrite the competing work. Inspect and reconcile it
deliberately (merge/rebase after reading its intent), rerun all impacted
verification, then integrate.

## Destructive git operations

Autonomous agents must never run, absent explicit human authorization in the
campaign prompt itself:

- `git push --force` / `-f` (including `--force-with-lease`) to shared branches;
- deletion of remote branches or tags;
- `git reset --hard` / `checkout -- .` over uncommitted competing work;
- history rewriting (`filter-repo`, interactive rebase of pushed commits);
- removal or overwrite of quarantine/failed save artifacts produced by the game.

## Incident recovery

If a guard, lock, or race check fires unexpectedly:

1. Stop mutating; capture diagnostics (`git status`, `git log`,
   `scripts/writer-lock.sh status`, `git log HEAD..origin/<branch>`).
2. Determine whether the cause is a stale lock, a legitimate competing session,
   or an identity mismatch.
3. Recover deliberately per the sections above; document what happened in the
   campaign report. After any collision event, rerun the full available gate set.

## Product invariants

Do not silently violate these:

- Walking/steps are the universal movement baseline.
- Real-world movement generates Vitality, which powers restoration.
- Running receives bounded optional bonuses; raw top speed is never an uncapped reward multiplier.
- Passive step earning must work without GPS.
- Rest never destroys progress.
- Regions are bounded; travel between regions is via map/transit, not seamless third-person traversal.
- Builder View and Explore View project the same canonical `RegionState`.
- Building transforms are persisted in state, never only in scene objects.
- Native iOS/Android code returns sensor facts; C# domain logic computes rewards.
- Core gameplay remains offline-capable.
- MVP is one polished region, not a broad unfinished world.

## Current milestone

Do not infer the milestone from the original roadmap checkbox state. Instead:

1. Consult `docs/IMPLEMENTATION_STATUS.md` - it is the authoritative, evidence-tiered
   record of what is implemented vs verified vs unverified.
2. Run the certification commands before starting new work:
   `dotnet test verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj`
   (and `scripts/verify-unity-editmode.ps1` when an editor is available).
3. Claim only evidence you actually produced; editor/device gates without the tool
   or hardware present stay marked UNVERIFIED.

## Architecture changes

Major changes require an ADR under `docs/adr/NNNN-short-title.md` and corresponding documentation updates.

## Definition of done

A feature is not done until relevant tests, save/load behavior, lifecycle/permission fallbacks, and documentation changes are included. Native or performance-sensitive work must be validated on physical mobile hardware.

Exactly-once rule for movement: one piece of real movement must never generate Vitality twice across passive polling, Expeditions, process restarts, or save reloads. Any change touching activity credit must extend the regression coverage in `ActivityServiceTests`, `AndroidCounterReconciliationTests`, and `SaveLoadTests`.

See `docs/AGENT_EXECUTION_GUIDE.md` for the full workflow.
