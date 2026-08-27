# Execution Prompt — M8.7 Canonical State & Certification Integrity Closure

Status: ACTIVE
Planned-From: main@e78ba78f24e77e7566b9ed3259878f6af83d24b5
Planner branch: agent/walk-game/m8.7-planner-20260827
Canonical OpenSpec: openspec/changes/m8.7-canonical-state-certification-integrity/
Target implementation branch: agent/walk-game/m8.7-<session-id>
Target milestone: M8 — Device Ready
Autonomous work budget: up to 12 hours

## Mission

Execute the complete M8.7 OpenSpec in one autonomous integrity-closure campaign.

This is not an M9 feature-expansion prompt. The 2026-08-27 re-audit found a new canonical save-graph defect family after M8.5/M8.6 planning: schema-compatible JSON can contain null or identity-inconsistent region/history elements that survive current SaveValidator behavior and can later crash boot or failed-save rollback. State integrity outranks playtest expansion under the repository roadmap.

The previous M8.6 executor also produced local commit d0c8687, but its first push was blocked because the tracked pre-push guard refuses a branch whose remote ref does not yet exist. During this planner session the remote branch agent/walk-game/m8.6-exec-20260826 was created at main@e78ba78 specifically so a still-existing local d0c8687 can be pushed by a normal fast-forward. Never assume d0c8687 is landed; prove it exists and inspect it.

Work the entire stack:
identity/reconcile -> prior M8.6 recovery -> fresh baseline -> reproduce save-graph failures -> structural invariant matrix -> repair/load/rollback hardening -> first-push guard repair -> carry forward remaining M8.6 evidence integrity -> whole-repo regression -> genuine editor/device lanes if available -> docs/OpenSpec -> detailed normal push.

Do not stop after the first fix while legitimate in-scope work remains. Do not fabricate work to occupy 12 wall-clock hours.

## Absolute repository boundary

This repository is quantdale/walk-game. It is not quantdale/simple-walk-game.

Before mutation:
    sh scripts/assert-repo-identity.sh
or:
    ./scripts/Assert-RepoIdentity.ps1

Stop on mismatch.

## Required reading before implementation

Read in full:
1. AGENTS.md
2. .agent/PLANNER_HANDOFF.md
3. this file
4. openspec/changes/m8.7-canonical-state-certification-integrity/audit.md
5. proposal.md
6. design.md
7. specs/integrity-closure/spec.md
8. tasks.md
9. docs/IMPLEMENTATION_STATUS.md
10. docs/MASTER_PLAN.md
11. docs/ROADMAP.md
12. docs/TECHNICAL_ARCHITECTURE.md
13. docs/DATA_MODEL.md
14. docs/TESTING_AND_PERFORMANCE.md
15. docs/AGENT_EXECUTION_GUIDE.md
16. docs/ACTIVITY_REWARD_SYSTEM.md
17. docs/MOBILE_ACTIVITY_INTEGRATION.md
18. docs/PRIVACY_SAFETY_ANTI_CHEAT.md
19. ADR 0007 through ADR 0011
20. the full M8.6 OpenSpec because any unresolved R1–R9 / E1–E10 requirement remains binding.

## Startup / reconciliation — mandatory

1. Prove identity.
2. Fetch origin and inspect current origin/main; do not assume e78ba78 is still head.
3. Record branch, HEAD, upstream, worktree, recent commits, open PRs/issues.
4. Inspect origin/agent/walk-game/m8.6-exec-20260826.
5. Check whether d0c8687 exists locally or remotely:
       git cat-file -e d0c8687^{commit}
   If it exists, inspect its full diff and ancestry before reuse.
6. If the previous local M8.6 branch contains d0c8687 and origin/agent/walk-game/m8.6-exec-20260826 is an ancestor, run the repository remote-advance guard and push that prior branch normally. The remote branch was pre-created at e78ba78 to make this possible. Never force.
7. Reconcile current main + equivalent M8.6 work into a new dedicated M8.7 implementation branch/worktree.
8. Acquire the writer lease before the first mutation.
9. Run fresh baseline gates and inventory external toolchains.

If d0c8687 cannot be found, do not recreate its summary as fact. Inspect current source and implement only requirements that are actually missing.

## Planner-confirmed findings to reproduce/disposition

### H1 — null current RegionState can pass validation and crash later

WorldState.GetOrCreateRegionState returns an existing dictionary value without checking for null. SaveValidator skips null RegionState values. A parseable save can therefore keep currentRegionId -> null and later dereference null during boot/seeding.

Required result:
- focused regression;
- load-boundary structural repair or explicit fail-closed classification;
- GetOrCreateRegionState defense in depth;
- boot-equivalent path no longer throws;
- no progression is fabricated.

### H2 — region dictionary key and RegionState.regionId can disagree

Current repair does not enforce one identity. Downstream code uses both forms.

Required result:
- deterministic canonical identity rule;
- regression for key/value mismatch;
- no split catalog/event/progression identity after repair.

### H3 — null recent Vitality transaction can break rollback

SaveValidator repairs the list container but not null elements. ProfileStateCopier.CopyTransaction dereferences every source element.

Required result:
- regression containing a null history element;
- drive a real failed PersistenceCoordinator commit;
- no unhandled exception in recovery;
- valid balance/history retained without minting progression.

### H4 — no full structural invariant matrix

Build and regression-lock a complete serializer-visible canonical graph matrix. Repair only structural impossibilities or already-defined safety values; do not indiscriminately sanitize legitimate data.

### H5 — first-push race guard deadlocks on new branches

The hook fetches the exact target ref and treats "ref absent" as generic failure, blocking first push. Standalone race scripts describe new-branch support but have the same fetch-first contradiction.

Required result:
- exact ref existence probe;
- absent ref positively proven => first normal push allowed;
- transport/auth uncertainty => fail closed;
- existing remote advancement/divergence => block;
- deletion => block;
- PowerShell/shell/hook parity;
- deterministic local bare-remote tests;
- no force path.

## M8.6 carry-forward

The previous session summary reported:
- exact pinned Unity toolchain preflight;
- serial-bound adb;
- idempotent uninstall;
- final smoke summary/logcat in finally;
- foreground/resumed activity evidence;
- Test-CertificationScripts 35/35.

Treat those as reconciliation hints, not authoritative source state.

After incorporating actual d0c8687/equivalent code, verify the full M8.6 contract still includes:
- fail-closed semantic EditMode/PlayMode XML validation;
- explicit semantic Unity compile/import verifier;
- exact Unity 6000.3.4f1 identity;
- Android build source/toolchain/APK provenance;
- exact adb serial everywhere;
- idempotent clean install;
- failure artifacts/final disposition;
- foreground/resumed package;
- source/APK/device cross-check;
- certification fixture regressions;
- honest evidence-tier separation.

If any locally executable requirement is still missing, finish it within M8.7.

## Fresh required baseline/final gates

Run from reconciled source and again from final source:
    dotnet test verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj
    ./scripts/verify-domain.ps1
    ./scripts/verify-unity-static.ps1
    ./scripts/verify-release-hygiene.ps1
    ./scripts/Test-AgentGuards.ps1
    ./scripts/Test-CertificationScripts.ps1   # when present after reconciliation
    git diff --check

Also run every newly added focused persistence/guard regression.

No historical count is a current PASS.

## Genuine editor/device lanes

If and only if legitimate prerequisites exist, continue through:
- Unity 6000.3.4f1 semantic import/compile;
- EditMode;
- PlayMode;
- Android IL2CPP ARM64 build;
- selected-target lifecycle smoke;
- physical step-counter exactly-once;
- physical Builder/Explore/permission/save-recovery UX;
- measured FPS/frame time/GC/memory/battery/thermal;
- iOS only with real macOS/Xcode/signing/device.

If a prerequisite is absent, record exact UNVERIFIED blocker once and move to another legitimate lane. Never bypass licensing, elevation, signing or device requirements.

## Scope exclusions

Do not add:
- Region 2;
- HealthKit/Health Connect;
- cloud/accounts/social/multiplayer;
- analytics/backend rollout;
- broad art/UI overhaul;
- speculative performance optimization without measurements;
- economy rebalance;
- unrelated package modernization.

Do not weaken exactly-once movement, privacy, offline-first behavior, one-writer rules, quarantine semantics, or safe Git history.

## 12-hour continuation policy

Use tasks.md section 12 as the detailed schedule.

Operationally:
- continue while legitimate M8.7 work remains;
- reproduce before fixing where feasible;
- add regression coverage for each Critical/High defect;
- do not repeatedly retry unchanged external blockers;
- do not stop after one successful patch if later in-scope work is executable;
- finish early if the entire executable scope is genuinely complete;
- if the budget ends with productive work remaining, leave an exact continuation point.

## Completion protocol

Before completion:
1. rerun all available final gates;
2. re-audit every changed call path;
3. update docs and ADRs based on actual behavior;
4. update M8.7 OpenSpec checkboxes/evidence;
5. append a detailed executor report to this file with:
   - planned/start/reconciled/final SHAs;
   - prior M8.6 recovery disposition and d0c8687 availability;
   - branch/worktree/lease;
   - every reproduced defect/root cause/fix;
   - structural invariant matrix;
   - exact fresh test counts;
   - guard scenario matrix;
   - M8.6 certification-harness disposition;
   - editor/build/device evidence or blockers;
   - docs/ADR changes;
   - remaining risks;
   - next-campaign recommendation.
6. mark Status COMPLETE only when all locally executable work is done; external tiers may remain explicitly UNVERIFIED.
7. fetch/check remote advancement.
8. commit with a detailed full-session report and push the M8.7 implementation branch normally.
9. never force-push.

## Next campaign decision

Only recommend M9 Closed Playtest Readiness when M8.7 closes all discovered Critical/High canonical-state/integration defects and no executed device/editor evidence exposes a release blocker.

If a real measured exactly-once, performance, build or UX blocker remains, recommend a focused campaign on that blocker. Do not jump to Region 2.
