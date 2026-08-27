# M8.7 Tasks — Canonical State & Certification Integrity Closure

Status: ACTIVE
Autonomous work budget: up to 12 hours
Executor rule: continue across all locally executable workstreams. Do not stop after the first fix. Do not pad wall-clock time with unrelated work.

## 0. Identity, prior-session recovery and one-writer setup

- [ ] Run the repository identity guard; stop on mismatch.
- [ ] Read AGENTS.md, PLANNER_HANDOFF, this entire M8.7 OpenSpec, IMPLEMENTATION_STATUS, MASTER_PLAN, ROADMAP, TECHNICAL_ARCHITECTURE, DATA_MODEL, TESTING_AND_PERFORMANCE, AGENT_EXECUTION_GUIDE and ADR 0007–0011.
- [ ] Fetch origin and record origin/main, HEAD, branch, worktree, recent commits, open PRs/issues.
- [ ] Inspect origin/agent/walk-game/m8.6-exec-20260826.
- [ ] Test whether d0c8687 exists locally/remotely. If it exists, inspect its full diff and ancestry.
- [ ] If the previous local branch can now be pushed as a normal fast-forward to the pre-created remote branch, do so only after the existing guards prove safety. Never force.
- [ ] Build the M8.7 implementation branch/worktree from current authoritative main plus deliberately reconciled equivalent M8.6 work.
- [ ] Acquire the writer lease before mutation.
- [ ] Record start SHA and exact reconciliation decisions.

## 1. Fresh baseline

Run and record fresh:
- [ ] dotnet test verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj
- [ ] scripts/verify-domain.ps1
- [ ] scripts/verify-unity-static.ps1
- [ ] scripts/verify-release-hygiene.ps1
- [ ] scripts/Test-AgentGuards.ps1
- [ ] scripts/Test-CertificationScripts.ps1 if present after M8.6 reconciliation
- [ ] git diff --check
- [ ] environment inventory for Unity/license/modules, .NET, adb, Android toolchain, device and iOS prerequisites

Do not reuse historical PASS counts as current evidence.

## 2. Reproduce H1/H2/H3 before implementation

Create focused red tests/fixtures for:
- [ ] current-region dictionary key with null RegionState;
- [ ] unlocked non-current region with null RegionState;
- [ ] region key/value regionId mismatch;
- [ ] null recentVitalityTransactions element;
- [ ] failed PersistenceCoordinator commit after loading a durable save containing that null history element;
- [ ] repaired profile round-trip and repair idempotence.

Where a direct GameHost boot test requires Unity, reproduce the unsafe domain/persistence call chain headlessly and add PlayMode coverage later if a licensed editor exists.

## 3. Canonical structural invariant matrix

Audit every serializer-visible field and record a matrix in audit/report or tests:
- [ ] PlayerProfile root references and history list;
- [ ] WorldState current/unlocked/region map;
- [ ] RegionState sets/maps;
- [ ] BuildingState placement;
- [ ] ProducerState;
- [ ] ActivitySyncState and dedup containers;
- [ ] activeSession legitimate nullability;
- [ ] AchievementState;
- [ ] PlayerSettings;
- [ ] VitalityTransaction list elements.

For each, mark: required-repair, legitimate-null, prune, fail-closed, or preserve.

## 4. Implement minimal save-graph repairs

- [ ] Make GetOrCreateRegionState self-heal an existing null value.
- [ ] Normalize or fail closed on region key/value identity mismatch according to design.md.
- [ ] Ensure the current region resolves non-null after repair.
- [ ] Ensure no null RegionState remains in accepted canonical state.
- [ ] Remove/contain null VitalityTransaction history elements without changing balance.
- [ ] Add SaveValidationReport structural repair evidence if useful.
- [ ] Preserve all valid progress and existing exactly-once stores.
- [ ] Do not add a schema bump unless valid schema-v1 semantics actually change.
- [ ] Update ADR 0007 if the persistence boundary contract materially changes.

## 5. Rollback containment

- [ ] Drive a real failing-save PersistenceCoordinator path using repaired malformed source.
- [ ] Prove no unhandled exception escapes rollback.
- [ ] Prove surviving object identity remains stable where M8.5 requires it.
- [ ] Prove stale target-only nested keys are still removed.
- [ ] Prove dedup indexes remain canonical.
- [ ] Prove null-history repair cannot mint or duplicate Vitality.
- [ ] Add negative tests for unrecoverable structural cases if any are classified fail-closed.

## 6. First-push guard repair

- [ ] Reproduce the absent-remote-ref failure in a local bare-remote fixture.
- [ ] Implement exact remote-ref existence probing that distinguishes absent ref from transport/auth failure.
- [ ] Apply equivalent semantics to pre-push, PowerShell remote-advance and shell remote-advance implementations.
- [ ] Preserve deletion refusal.
- [ ] Preserve ancestor/race check for existing branches.
- [ ] Add Test-AgentGuards scenarios for first push, second fast-forward, remote advance, divergence, exact-ref matching and unreachable origin.
- [ ] Ensure all tests use local filesystem remotes and keep egress disabled.
- [ ] Do not weaken the no-force-push policy.

## 7. Reconcile/complete M8.6 locally executable evidence work

After reading the actual reconciled code, not the summary:
- [ ] preserve exact Unity-pin preflight if present;
- [ ] preserve serial-bound adb and foreground/resumed checks if present;
- [ ] preserve idempotent uninstall and final-summary evidence if present;
- [ ] verify shared semantic EditMode/PlayMode XML validation exists and has false-green fixtures;
- [ ] verify explicit semantic compile/import gate exists;
- [ ] verify Android build provenance manifest exists and binds source/toolchain/APK;
- [ ] verify smoke evidence cross-checks source/APK/target identity;
- [ ] extend Test-CertificationScripts for any missing M8.6 false-green scenario;
- [ ] do not duplicate equivalent d0c8687 work.

## 8. Whole-repository regression sweep

- [ ] Re-scan all C# and native/runtime surfaces touched.
- [ ] Search for TODO/FIXME/HACK/NotImplementedException/debug bypasses introduced by this campaign.
- [ ] Audit every direct caller of GetOrCreateRegionState.
- [ ] Audit all ProfileStateCopier call sites.
- [ ] Audit all SaveValidator entry points and serializer load paths.
- [ ] Audit all recentVitalityTransactions readers/writers.
- [ ] Re-check activity exactly-once tests even if movement code was not intentionally changed.
- [ ] Verify release logging/privacy invariants.
- [ ] Verify no sibling-repository contamination.

## 9. Optional real M8 certification lanes — only when prerequisites exist

If licensed Unity 6000.3.4f1 exists:
- [ ] semantic import/compile;
- [ ] EditMode with semantic XML proof;
- [ ] PlayMode runtime certification.

If Android Build Support exists:
- [ ] IL2CPP ARM64 development build with provenance;
- [ ] selected-target lifecycle smoke.

If genuine step-counter hardware exists:
- [ ] physical exactly-once lifecycle cases;
- [ ] touch/safe-area/Builder/Explore UX;
- [ ] measured performance/GC/memory/battery/thermal.

If real macOS/Xcode/signing/device exists:
- [ ] iOS generation/build/plist/device checks.

Otherwise record one exact blocker per tier and move on.

## 10. Mandatory final gates

From final source state:
- [ ] repository identity guard
- [ ] dotnet test
- [ ] verify-domain
- [ ] verify-unity-static
- [ ] verify-release-hygiene
- [ ] Test-AgentGuards
- [ ] Test-CertificationScripts if present
- [ ] every new focused save-integrity test
- [ ] git diff --check
- [ ] real editor/build/device gates only when executable

No unavailable tier is marked PASS.

## 11. Documentation / OpenSpec closure

- [ ] Update IMPLEMENTATION_STATUS with exact fresh counts and evidence tiers.
- [ ] Update DATA_MODEL with structural repair invariants if behavior changed.
- [ ] Update TESTING_AND_PERFORMANCE with new test/gate semantics.
- [ ] Update AGENT_EXECUTION_GUIDE for safe first-push semantics.
- [ ] Update ADR 0007 if required.
- [ ] Mark M8.7 COMPLETE only when every locally executable requirement is done and external blockers are explicit.
- [ ] Append a detailed executor report to .agent/EXECUTION_PROMPT.md.
- [ ] Record whether M9 is now recommended and why.

## 12. 12-hour continuation budget

Suggested order, not a wall-clock quota:
- Hour 0–1: identity, reconciliation, recover/push prior M8.6 commit if safe, lease, fresh baseline.
- Hour 1–3: red tests and full persisted-graph invariant matrix.
- Hour 3–5: SaveValidator/WorldState repair implementation and focused regressions.
- Hour 5–6.5: rollback containment/fault-injection and serializer round-trip stress.
- Hour 6.5–8: first-push guard fix + deterministic guard matrix.
- Hour 8–9.5: M8.6 reconciliation/completion of evidence wrappers and certification tests.
- Hour 9.5–10.5: whole-repo regression sweep and full headless gates.
- Hour 10.5–11.25: genuine editor/device lanes if prerequisites exist; otherwise additional legitimate corruption/fault cases only.
- Hour 11.25–12: docs/OpenSpec/evidence matrix, remote-advance check, detailed commit and normal push.

Continuation rules:
- Do not stop after one green test while later in-scope work remains.
- Do not retry unchanged external blockers repeatedly.
- Do not invent work merely to consume 12 hours.
- If all legitimate work is complete early, close honestly.
- If the budget ends with productive work remaining, leave an exact continuation point and artifacts.

## 13. Completion / push

- [ ] Fetch and re-check remote advancement.
- [ ] Reconcile competing commits deliberately.
- [ ] Commit a detailed full-session report.
- [ ] Push the implementation branch normally.
- [ ] Never force-push or delete remote refs.
