# Execution Prompt — Repository Isolation, Concurrency Safety & Whole-Repo Breakage Audit

**Status:** COMPLETE
**Planned-From:** `main@1012fc2069bf08b2a08b01f0544e4ba2bbd43753`
**Target branch:** `main`
**Campaign type:** repair + repository isolation + agent-infrastructure hardening
**Priority:** Critical — wrong-repository execution risk and concurrent-writer lost-update risk

## Mission

Deep-audit `quantdale/walk-game`, find and repair whatever actually broke,
strengthen `AGENTS.md`, add enforceable repository-identity protection, prevent
concurrent writers from silently overwriting one another, protect pushes from
remote races, and fully verify, commit, and push the repair.

## Absolute repository boundary

This repository is `quantdale/walk-game`. It is NOT `quantdale/simple-walk-game`.
The sibling has a similar product concept but is an independent project. Never
transfer assumptions, campaign state, SHAs, implementation status, roadmap
state, source files, or prompts between them; never mutate the sibling from this
session. If repository identity cannot be proven exactly, STOP before modifying
anything.

## Workstreams

1. **Phase 0 identity check (fail closed)** — git root, normalized origin,
   fingerprints (`Assets/`, `Packages/`, `ProjectSettings/`,
   `verification/WalkGame.Domain.Tests/`, `docs/IMPLEMENTATION_STATUS.md`,
   `AGENTS.md`); never trust the directory name.
2. **Machine-readable identity** — `.repo-identity.json` plus reusable guard
   script(s) under `scripts/` validating root, identity file, normalized
   origin, expected slug, fingerprints, and `GITHUB_REPOSITORY` under CI;
   Windows/Unix support where practical; failure terminates the operation.
3. **Harden AGENTS.md** — keep all existing rules; first section becomes the
   repository identity contract; add sibling isolation, concurrent writers,
   worktree ownership, session writer lock, start SHA, remote advancement,
   destructive git operations, incident recovery; keep policy harness-agnostic.
4. **Single-writer lock** — atomic untracked lease under `.git/` capturing slug,
   session id, hostname, PID, branch, start SHA, timestamp; one mutable writer
   per worktree; no silent stealing; deliberate recovery only; release on
   normal completion.
5. **Worktree concurrency contract** — one writer = one branch = one worktree;
   documented and scripted where useful.
6. **Lost-update protection** — record start SHA; fetch before integration;
   unexpected remote advancement stops automatic integration; no force push
   without explicit human authorization.
7. **Hooks + CI** — tracked `.githooks/pre-commit` + `pre-push`
   (`core.hooksPath .githooks`) running identity verification; CI fails when
   `GITHUB_REPOSITORY != quantdale/walk-game`; hooks are one layer only.
8. **Agent infrastructure audit** — `.agent/`, `.agents/`, `.claude/`,
   `.kimi-code/`, `.opencode/`, `docs/AGENT_EXECUTION_GUIDE.md`, `scripts/`:
   stale sibling prompts, wrong paths/repos/branches, stale SHAs, unsafe
   auto-push, preflight bypasses, multi-writer setups, goal flows that skip
   identity revalidation. Every autonomous entry point passes through the guard.
9. **Deep whole-repo breakage audit** — Unity assets/scenes/metas/GUIDs, domain,
   application, persistence, activity providers, exactly-once behavior,
   save/load, migrations, backup recovery, failure containment, Builder/Explore
   sync, RegionState, producers, Expeditions, onboarding, UI, platform adapters,
   lifecycle, verification harness, editor scripts, CI, documentation. Search
   for partially reverted implementations, stale duplicates, missing metas,
   broken references, missing serialization fields, incomplete persistence
   copies, startup failure paths creating writable fresh state, failed rollback,
   success-after-persistence-failure UI, cursor/dedup divergence, canonical-state
   divergence, unverified runtime assumptions, documentation claiming evidence
   that was not produced. Do not stop after unit tests pass.
10. **Repair** every Critical/High regression found and any Medium issue that
    threatens correctness, state integrity, buildability, repository isolation,
    or future autonomous campaigns. No test suppression. Regression tests for
    discovered defects. Preserve fixed architecture and gameplay invariants;
    ADR required for major changes.
11. **Guard self-tests** — deterministic proof that correct identity passes;
    sibling identity / wrong origin / wrong GITHUB_REPOSITORY fail; nested
    invocation finds root; HTTPS and SSH remotes pass; concurrent acquisition
    fails; release permits another session; stale locks are not silently
    stolen; unexpected remote advancement is detected; tests never touch the
    real remote.
12. **Cross-repository contamination check** — search config/agent/docs for
    `quantdale/simple-walk-game`; occurrences must be part of the explicit
    protection mechanism or justified documentation; there must be no execution
    prompt directing work on the sibling.
13. **Final certification** — clean tree, `git diff --check`, full available
    gates, complete diff inspection, push without force.

## Validation gates

Run every genuinely available gate and record exact results:

1. `dotnet test verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj`
2. `scripts/verify-domain.ps1`
3. `scripts/verify-release-hygiene.ps1`
4. `scripts/verify-unity-static.ps1`
5. `pwsh scripts/Test-AgentGuards.ps1`
6. `git diff --check`
7. Unity EditMode/PlayMode **only if a licensed pinned editor session is actually
   available**; otherwise record UNVERIFIED with reproducible commands.
8. Device checks only when suitable hardware/environment exists; never fabricate.

## Completion / acceptance gates

1. Identity proven before any mutation; guards enforce it on commit, push, and CI.
2. Single-writer semantics enforceable and tested; stale recovery deliberate.
3. Remote races detected before integration; force/deletion refused by hooks.
4. Every autonomous entry point routes through the identity guard.
5. All Critical/High findings from the whole-repo audit repaired with regression
   coverage; Medium state-integrity issues repaired or explicitly deferred with
   rationale.
6. Contamination grep clean outside the protection mechanism.
7. `docs/IMPLEMENTATION_STATUS.md` updated with exact evidence and no
   overclaiming; ADR added for materially architectural decisions; relevant
   contract docs corrected where they contradict code.
8. Working tree clean after final commit and push; detailed commit messages.

## Git and reporting requirements

- Never force-push. Do not overwrite unrelated concurrent work.
- At completion, flip this file to `Status: COMPLETE` and append an executor
  report: start/final SHAs, systems changed, root causes, tests, exact
  validation results, UNVERIFIED tiers, deferred follow-ups.
- The final commit message doubles as the full session report.

## Executor report (M8.2 campaign complete)

- **Start SHA:** `1012fc2` (planned from the same commit; no intervening work landed).
  **Final SHA:** this commit.
- **Systems added:** `.repo-identity.json`; `scripts/Assert-RepoIdentity.ps1` +
  `scripts/assert-repo-identity.sh` (fail-closed identity guard);
  `scripts/WriterLock.ps1` + `scripts/writer-lock.sh` (single-writer lease under
  untracked `.git/walk-game-writer.lock`); `scripts/Check-RemoteAdvance.ps1` +
  `scripts/check-remote-advance.sh` (lost-update guard); `.githooks/pre-commit`,
  `.githooks/pre-push`, `scripts/setup-hooks.*`; `scripts/Test-AgentGuards.ps1`
  deterministic suite; CI `repository-identity` + `agent-guards` jobs.
- **Systems repaired:** `GameHost.DurableCommitResolved` broadcast;
  `FeedbackController` deferred durable-cue queue;
  `AppFlowController.Interact`/`ConfirmBuildingMove` outcome-gated feedback and
  truthful rollback copy; `UiComposer` celebration cues deferred, pure onboarding
  derivation, debug-reset containment; `SaveValidator` milestone-id/distance/
  producer repairs (+2 tests); dead `DeleteAll` removed (ADR 0007 amended).
  AGENTS.md identity-first contract; ADR 0008; harness goal adapters route
  through the guard; TECHNICAL_ARCHITECTURE §14 corrected; IMPLEMENTATION_STATUS
  refreshed with exact evidence.
- **Root causes fixed:** nothing proved repository identity before mutation;
  nothing prevented two mutable sessions in one worktree or a force-shaped push
  over competing remote work; presentation could claim success for actions whose
  persistence had failed or reverted (High: lore rollback UX); load-time
  validation tolerated null/negative producer/milestone/distance state that
  later crashed reward paths or poisoned bonus math.
- **Breakage attribution:** no evidence of wrong-repository execution damage was
  found in the tree (contamination grep clean; all agent-infra files reference
  only walk-game); findings were pre-existing correctness/documentation defects
  plus missing concurrency safeguards, not collision artifacts.
- **Validation:** `dotnet test ...` → **146/146 PASS**; `verify-domain.ps1` →
  PASS; `verify-release-hygiene.ps1` → PASS (61 runtime sources);
  `verify-unity-static.ps1` → PASS (99 assets / 99 metas);
  `Test-AgentGuards.ps1` → **36/36 PASS**; live identity guard exit 0;
  `git diff --check` clean.
- **UNVERIFIED gates (unchanged environment blockers):** Unity EditMode/PlayMode
  (account-level licensing), Android/iOS device tiers. Reproducible commands are
  recorded in docs/IMPLEMENTATION_STATUS.md.
- **Deliberately deferred:** URP/Graphics/Input settings commit-after-setup,
  unused Unity module trimming, re-paying drained Android windows after failed
  commits, ActivityTicker no-op commit — see IMPLEMENTATION_STATUS follow-ups.
