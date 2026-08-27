# M8.8 Proposal — Pre-Playtest Integrity & Unity Bring-Up Closure

Status: ACTIVE
Planned-From: main@cf260d04fefbb2d5e7da265de5ae03a9aa768a0a
Depends on: M8.7 COMPLETE
Target: final pre-M9 closure
Autonomous work budget: up to 12 hours

## Problem

Walk Game's engine-free domain and canonical-state coverage is strong, but current main is not ready for a pure closed-playtest campaign.

The 2026-08-27 whole-tree audit found:
- a confirmed Editor-assembly namespace/semantic compile defect;
- a SaveMigrator success path that does not guarantee the save reached Current;
- a mismatch between claimed semantic-compile evidence policy and the actual tracked verification scripts;
- first-import URP/project state that is generated during setup instead of fully represented by clean-checkout source;
- platform permission/callback lifetime questions that require targeted reproduction before stronger device-readiness claims.

Starting M9 without closing these would spend playtest time discovering bring-up and compatibility defects that should be caught before a tester ever receives a build.

## Proposed change

Execute one ordered M8.8 campaign:

1. Reconcile from current main and rerun every available baseline.
2. Fix the confirmed Editor semantic compile defect and sweep all Unity-only assemblies.
3. Add a dedicated, fail-closed pinned-Unity semantic import/compile certification gate.
4. Repair SaveMigrator so success has a strict schema postcondition and unsupported pre-v1 material fails closed.
5. Materialize or explicitly certify deterministic first-import project/URP state using a real Unity editor; never fabricate generated assets by hand.
6. Reproduce Android denial/restart semantics and iOS provider/callback lifetime behavior before making platform changes.
7. Close small canonical numeric/audit invariants that are cheap, deterministic and directly supported by the audit.
8. Run real EditMode/PlayMode/Android/iOS/device/performance lanes when prerequisites actually exist.
9. Rerun the complete regression matrix, update evidence/docs/OpenSpec, commit and push a detailed report.

## Goals

- The Unity Editor assembly must be semantically compilable under the pinned editor.
- A standalone semantic compile/import gate must exist and fail closed.
- `SaveMigrator.TryMigrateToCurrent == true` must imply `schemaVersion == Current`.
- Unsupported lower schemas must not be silently accepted.
- Every migration step must demonstrably advance or fail.
- A clean checkout must have a documented, reproducible path to the exact project state that is certified/built.
- Platform permission/provider lifecycle claims must be separated into source, editor, emulator and physical-device evidence tiers.
- Vitality/resource/score mutations must retain their audit/numeric invariants at boundary values.
- No lower evidence tier may be promoted by inference.
- M9 is recommended only when no pre-playtest Critical/High defect remains.

## Non-goals

Do not add:
- Region 2 or new restoration content;
- HealthKit/Health Connect;
- cloud save/accounts/social/multiplayer;
- analytics/backend rollout;
- economy rebalance;
- broad art redesign;
- unrelated package upgrades;
- speculative performance optimizations without measurement.

Do not weaken:
- exactly-once movement delivery;
- fail-closed persistence;
- player save quarantine/recovery;
- passive-steps-without-GPS policy;
- repository identity/writer/race guards;
- safe Git history.

## Expected implementation surface

Likely:
- `Assets/WalkGame/Editor/WalkGameEditorTools.cs`;
- `Assets/WalkGame/Persistence/SaveMigrator.cs`;
- save/migration tests;
- a new semantic Unity compile/import wrapper plus certification helper/tests;
- project-generated Unity settings/assets only after a real editor creates them;
- Android provider/tests if restart semantics reproduce;
- iOS provider/native bridge/tests only when evidence requires a change;
- `VitalityLedger.cs`, `RewardApplier.cs` and focused tests for secondary invariants;
- CI/scripts README and testing/status documentation;
- this OpenSpec and `.agent/EXECUTION_PROMPT.md`.

Adjacent files may be changed only when root-cause evidence requires it.

## Success criteria

M8.8 is complete when:
- every H1-H4 finding in `audit.md` has a tested disposition;
- H1 is fixed in source and, when a licensed editor is available, proven by semantic compile;
- the semantic compile/import gate exists, produces current-run evidence and rejects false green states;
- SaveMigrator has strict success/progress/version tests;
- first-import URP/project mutation is either eliminated by committing editor-generated canonical state or explicitly proven deterministic and bound to evidence;
- Android/iOS platform findings are reproduced and fixed where possible, or remain honestly UNVERIFIED at the correct external tier;
- M1-M2 are closed or explicitly dispositioned with a written invariant;
- all available final gates pass from final source;
- no new Critical/High regression is left open;
- docs distinguish implemented fixes from evidence actually executed;
- the implementation branch is committed/pushed normally without force.

## Next milestone decision

If the above is green and no real editor/device run exposes a blocker, activate M9 Closed Playtest Readiness / Validation.

If a real compile, build, lifecycle, exactly-once, UX or performance blocker appears, create a focused campaign for that measured issue instead. Do not jump to content expansion.
