# Execution Prompt — M8.8 Pre-Playtest Integrity & Unity Bring-Up Closure

Status: ACTIVE
Planned-From: main@cf260d04fefbb2d5e7da265de5ae03a9aa768a0a
Planner branch: agent/walk-game/m8.8-planner-20260827
Canonical OpenSpec: openspec/changes/m8.8-pre-playtest-integrity-and-unity-bringup/
Target implementation branch: agent/walk-game/m8.8-<session-id>
Target milestone: M8.8 — final pre-M9 closure
Autonomous work budget: up to 12 hours

## Mission

Execute the complete M8.8 OpenSpec as one autonomous pre-playtest integrity campaign.

Do NOT jump directly to M9. The deep audit of current main found two confirmed pre-playtest defects plus an evidence/reproducibility gap:

1. `Assets/WalkGame/Editor/WalkGameEditorTools.cs` references `GraphicsSettings` and `IPostprocessBuildWithReport` without importing their actual Unity namespaces. Fix the source and prove semantic compilation when the pinned editor is available.
2. `SaveMigrator.TryMigrateToCurrent` can break out of its lower-schema loop and return true while `schemaVersion < Current`. Make success mean the profile is exactly Current, reject unsupported pre-v1 material, and regression-lock migration progress.
3. The tracked verification surface has no dedicated semantic Unity import/compile gate even though prior campaign requirements describe one. Add a real fail-closed gate so static structure can no longer masquerade as semantic compilation.
4. First-import/project setup creates URP/project state that is not currently represented by the clean tracked tree. On real Unity, materialize and classify that state or prove deterministic generation/provenance; never hand-fabricate Unity serialized assets.

The audit also identified Android denial-after-restart and iOS callback/provider-lifetime risks. Reproduce/certify those before changing native behavior. Close the smaller Vitality reason-code and unchecked reward-arithmetic invariants after the higher-priority work.

Continue through all legitimately executable lanes for up to 12 hours. Do not stop after one successful patch while later in-scope work remains. Do not add unrelated work merely to consume time.

## Absolute repository boundary

This repository is `quantdale/walk-game`.

It is NOT `quantdale/simple-walk-game`.

Before any mutation run:
    sh scripts/assert-repo-identity.sh
or:
    ./scripts/Assert-RepoIdentity.ps1

Stop on mismatch.

## Mandatory reading

Read in full before implementation:
1. `AGENTS.md`
2. `.agent/PLANNER_HANDOFF.md`
3. this file
4. `openspec/changes/m8.8-pre-playtest-integrity-and-unity-bringup/audit.md`
5. `proposal.md`
6. `design.md`
7. `specs/pre-playtest-integrity/spec.md`
8. `tasks.md`
9. `docs/IMPLEMENTATION_STATUS.md`
10. `docs/MASTER_PLAN.md`
11. `docs/ROADMAP.md`
12. `docs/TECHNICAL_ARCHITECTURE.md`
13. `docs/DATA_MODEL.md`
14. `docs/TESTING_AND_PERFORMANCE.md`
15. `docs/AGENT_EXECUTION_GUIDE.md`
16. `docs/ACTIVITY_REWARD_SYSTEM.md`
17. `docs/MOBILE_ACTIVITY_INTEGRATION.md`
18. `docs/PRIVACY_SAFETY_ANTI_CHEAT.md`
19. ADR 0003, 0005, 0007, 0009, 0010 and 0011
20. M8.6/M8.7 OpenSpec as historical constraints where they remain applicable.

## Startup / reconciliation

1. Prove repository identity.
2. Fetch origin and inspect current `origin/main`; do not assume planned-from is still head.
3. Record HEAD, branch, upstream, worktree, recent commits, open PRs/issues and dirty state.
4. If main advanced after `cf260d04fefbb2d5e7da265de5ae03a9aa768a0a`, inspect every intervening commit and preserve equivalent newer work.
5. Create one dedicated M8.8 implementation branch/worktree from reconciled current main.
6. Acquire the repository writer lease before mutation.
7. Run the fresh baseline and environment inventory in `tasks.md` sections 0-1.
8. Do not reuse historical PASS counts as current evidence.

## Priority order

Work in this order unless a reproduced dependency forces a narrower reorder:

### Priority A — confirmed source blockers
- H1 Editor namespace/semantic compile.
- H2 SaveMigrator false-success lower schema.
- H3 dedicated semantic Unity compile/import gate.
- H4 first-import/URP clean-checkout reproducibility.

### Priority B — platform lifecycle reproduction
- Android denial -> process restart -> refresh/request state model.
- iOS managed/native callback lifetime, pending query teardown and provider-generation ownership.

### Priority C — smaller canonical integrity
- non-empty reason code for successful Vitality spend;
- overflow-safe resource/region-score mutation;
- null-safe shader construction/disposition during Unity/build sweep.

### Priority D — genuine certification
When prerequisites actually exist:
- semantic Unity compile/import;
- EditMode;
- PlayMode;
- Android IL2CPP ARM64 build + provenance;
- selected-target lifecycle smoke;
- physical step-counter exactly-once;
- touch/UX/performance/battery/thermal;
- iOS only with real macOS/Xcode/signing/device.

If a prerequisite is absent, record one precise UNVERIFIED blocker and move to another legitimate lane. Never bypass Unity licensing, UAC/elevation, signing or hardware requirements.

## Required source behaviors

### Editor semantic compilation
Use the actual Unity namespaces/qualification. Do not hide the compile defect with reflection or a text-check allowlist.

Add a dedicated semantic import/compile wrapper that:
- proves exact Unity 6000.3.4f1;
- binds to current source SHA/dirty state;
- produces fresh log/evidence;
- fails on launch/import/compiler errors and stale evidence;
- surfaces unexpected project mutation;
- has deterministic false-green fixture tests.

### Save migration
A true return from `TryMigrateToCurrent` MUST imply `profile.schemaVersion == SaveSchemaVersions.Current`.

Current v1 is the initial defined schema. Explicit v0/negative material must fail closed unless a real documented migration is implemented.

Every future migration iteration must advance exactly as designed or fail. Never assign a version number merely to reinterpret unknown data.

### First-import project state
With genuine Unity, capture the first setup/import diff. Track stable canonical editor-generated state needed for reproducibility or prove deterministic idempotent generation and bind it into evidence.

Do not manually synthesize opaque `.asset`/project serialized data without Unity.

### Android/iOS
Reproduce platform-specific behavior before rewriting native/provider code. Keep headless/source evidence distinct from physical runtime evidence.

Preserve ADR 0011 ownership and the existing exactly-once activity transaction path.

### Canonical numeric/audit invariants
No successful Vitality spend without an audit reason.
No unchecked overflow that can wrap resources or region scores.

## Mandatory baseline and final gates

Run from fresh reconciled source and rerun from final source:
    dotnet test verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj
    ./scripts/verify-domain.ps1
    ./scripts/verify-unity-static.ps1
    ./scripts/verify-release-hygiene.ps1
    ./scripts/Test-AgentGuards.ps1
    ./scripts/Test-CertificationScripts.ps1
    git diff --check

Also run:
- all new migration/ledger/reward tests;
- semantic compile-wrapper fixture tests;
- actual semantic Unity compile/EditMode/PlayMode/build/device tiers when genuinely executable.

No historical count is a current PASS.

## Scope exclusions

Do not add:
- Region 2;
- HealthKit or Health Connect;
- cloud/accounts/social/multiplayer;
- analytics/backend rollout;
- broad art/UI redesign;
- economy rebalance;
- unrelated package upgrades;
- speculative optimization without measurement.

Do not weaken:
- exactly-once movement;
- fail-closed save recovery/quarantine;
- offline-first behavior;
- no-GPS passive movement;
- repository identity/writer/race guards;
- safe Git history.

## 12-hour continuation policy

Use `tasks.md` section 13 as the detailed schedule.

Operational rules:
- continue while legitimate M8.8 work remains;
- reproduce before fixing platform behavior where feasible;
- add regressions for each confirmed Critical/High defect;
- do not repeatedly retry an unchanged external blocker;
- do not stop after one successful patch if later lanes are executable;
- finish early if all legitimate executable scope is truly complete;
- if the budget ends with productive work remaining, leave an exact continuation point and evidence.

## Completion protocol

Before closing:
1. rerun every available final gate from final source;
2. re-audit all changed call paths and the whole-repo regression checklist;
3. update docs/ADRs only for behavior/evidence actually changed;
4. update M8.8 OpenSpec tasks and evidence truthfully;
5. change this prompt from ACTIVE to COMPLETE only after all locally executable requirements are finished;
6. append a detailed executor report containing:
   - planned/start/reconciled/final SHAs;
   - branch/worktree/writer lease;
   - exact environment;
   - H1-H4/P1-P2/M1-M3 reproduction, root cause, fix/disposition;
   - semantic compile-gate artifacts/results;
   - migration state table and focused tests;
   - first-import/project-state diff and idempotence;
   - exact fresh test counts;
   - editor/build/device/iOS evidence or blockers;
   - performance evidence when run;
   - docs/ADR changes;
   - remaining risks;
   - next-campaign recommendation.
7. fetch/check remote advancement;
8. commit a detailed full-session report;
9. push the implementation branch normally;
10. never force-push or delete remote refs.

## Next-campaign decision

Recommend M9 Closed Playtest Readiness only if M8.8 closes every discovered locally executable Critical/High pre-playtest defect and no real semantic/editor/build/device run exposes a blocker.

If genuine evidence exposes a compile/build/lifecycle/exactly-once/UX/performance blocker, the next campaign must target that measured blocker.

Do not jump to Region 2.
