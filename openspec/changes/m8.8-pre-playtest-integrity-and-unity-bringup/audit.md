# M8.8 Audit — Pre-Playtest Integrity & Unity Bring-Up Closure

Status: COMPLETE planning package; executor disposition recorded in `reaudit-2026-08-28.md`
Planned-From: main@cf260d04fefbb2d5e7da265de5ae03a9aa768a0a
Audit date: 2026-08-27
Repository: quantdale/walk-game
Target milestone: M8.8 — Pre-Playtest Integrity & Unity Bring-Up Closure
Autonomous execution budget: up to 12 hours

## 1. Executive conclusion

Do not start a pure M9 closed-playtest campaign yet.

M8.7 correctly closed the canonical-state corruption family it targeted, but a new whole-tree audit from current main found two confirmed pre-playtest defects and several certification/reproducibility gaps that outrank player testing:

1. the Editor assembly contains unresolved Unity namespace references that should prevent semantic compilation;
2. SaveMigrator can return success while a profile remains below the current schema version;
3. the repository still lacks a dedicated semantic Unity import/compile gate capable of catching finding 1 before EditMode/PlayMode;
4. first-import URP/project setup is generated into the worktree rather than represented by a fully materialized clean-checkout project state;
5. Android denial classification across process restart and iOS callback/provider lifetime need real reproduction before device-readiness can be called trustworthy.

This package therefore defines one final pre-playtest closure campaign. M9 becomes eligible only after the confirmed defects are fixed and all executable semantic/editor/device evidence is truthful.

## 2. Repository truth at planning time

- Authoritative main: `cf260d04fefbb2d5e7da265de5ae03a9aa768a0a`.
- Recursive tracked inventory: 297 blobs, 56 directories, approximately 1.56 MB.
- Source/config inventory includes 88 C# files, Android Kotlin, iOS Objective-C++, 14 asmdefs, 2 Unity scenes, 108 Unity .meta files, PowerShell/shell verification tooling, CI, Git guards, docs/ADRs and OpenSpec state.
- Unity pin: `6000.3.4f1`.
- No missing asset/meta pair and no orphan meta was found in the recursive Assets inventory.
- No open pull request was present at planning time.
- M8.7 records 224/224 domain tests, 108/108 Unity-static asset/meta checks, 35/35 certification-script tests, release hygiene PASS, and editor/device/iOS tiers UNVERIFIED. These are historical evidence only; the executor must rerun current gates.
- M8.7 is COMPLETE. This M8.8 package is the only ACTIVE implementation campaign.

## 3. Audit method

Every tracked path was inventoried from the recursive Git tree. Runtime C#, editor code, test assemblies, native bridges, scenes, asmdefs, manifests, project settings, CI, verification scripts, Git safety tooling, persistence/activity/gameplay/UI/world composition, current OpenSpecs and architecture/status/testing documentation were reviewed against their declared contracts.

Unity .meta files were checked structurally as a complete set against their tracked assets rather than treated as independent product logic. Scene YAML and assembly references were inspected directly. Critical paths were traced through their callers and tests instead of relying on filenames or historical completion reports.

The review explicitly rechecked:
- save deserialize -> migrate -> validate -> load -> rollback;
- activity prepare/process/commit/resolve ownership;
- permission state/refinement across platform providers;
- GameHost composition and runtime teardown;
- editor project setup/build entry points;
- Unity static/EditMode/PlayMode wrappers;
- Android build/smoke evidence;
- native iOS query/session callbacks;
- Builder/Explore/UI projection;
- CI and repository guard coverage.

The M8.3-M8.7 exactly-once movement architecture remains coherent in this static pass. No newly discovered Critical/High duplicate-credit path supersedes the findings below.

## 4. Confirmed findings

### H1 — Editor assembly has a semantic compile blocker

File: `Assets/WalkGame/Editor/WalkGameEditorTools.cs`.

The file imports `UnityEditor`, `UnityEditor.Build.Reporting` and `UnityEngine`, but it references:
- `GraphicsSettings.defaultRenderPipeline`, whose type lives in `UnityEngine.Rendering`;
- `IPostprocessBuildWithReport`, whose interface lives in `UnityEditor.Build`.

Neither required namespace is imported and neither symbol is fully qualified. This is not a style issue: under the Unity API namespace contract these identifiers are unresolved in that source file.

M8.6 recorded this as a predicted R1 issue because no licensed editor was available. M8.7 carried forward certification work but did not fix this source. Current static validation can still return green because it does not semantically compile the Editor assembly.

Required disposition:
- add a focused source fix;
- run a real pinned-Unity semantic compile/import when available;
- add a repository gate that makes this class of false-green materially harder to reintroduce;
- sweep every Unity-only assembly after the first compile result rather than stopping at the first compiler error.

Severity: HIGH because the project setup/build tooling is itself in the Editor assembly and cannot be trusted until it compiles.

### H2 — SaveMigrator can report success without reaching Current

File: `Assets/WalkGame/Persistence/SaveMigrator.cs`.

Current behavior:
- rejects only `schemaVersion > SaveSchemaVersions.Current`;
- enters a `while (profile.schemaVersion < Current)` loop;
- immediately `break`s because no migration step exists;
- sets `error = null` and returns true.

With current schema v1, an explicitly serialized schema 0 or negative value can therefore be accepted while the profile remains below Current. This violates TECHNICAL_ARCHITECTURE's sequential migration contract and makes the method's success result semantically false.

Required disposition:
- define the minimum-supported schema explicitly;
- because v1 is the initial real schema, reject unsupported pre-v1 material unless a real deterministic migration is introduced;
- guarantee that success implies `profile.schemaVersion == SaveSchemaVersions.Current`;
- guarantee every migration loop iteration advances exactly one schema step or fails;
- add explicit tests for current, newer, zero, negative and non-progressing/missing migration behavior.

Severity: HIGH because schema truth is the first persistence compatibility boundary.

### H3 — No dedicated semantic Unity compile/import gate exists in the tracked script surface

The tracked scripts contain `verify-unity-static.ps1`, `verify-unity-editmode.ps1` and `verify-unity-playmode.ps1`, but no dedicated semantic import/compile verifier.

M8.6/M8.7 requirements refer to an explicit semantic compile/import verifier, yet current source can contain H1 while the static gate remains green. EditMode/PlayMode do compile incidentally when they can run, but they conflate import/compiler failure with test execution and do not provide the standalone evidence tier the OpenSpec claims.

Required disposition:
- implement a fail-closed semantic import/compile command for pinned Unity 6000.3.4f1;
- preserve a fresh full editor log and machine-readable provenance;
- reject wrong editor identity, stale evidence, compiler errors, import failures and unexpected project mutation;
- regression-test the wrapper's false-green semantics without pretending to emulate Unity itself;
- make docs use the actual tracked gate name.

Severity: HIGH as an evidence-integrity defect because it let H1 survive completed hardening campaigns.

### H4 — Clean-checkout Unity project state is not fully materialized

The recursive tree contains no `Assets/Settings/URP-HighFidelity.asset`.

`WalkGameEditorTools.ConfigureUrp()` creates that asset on first setup and assigns it to `GraphicsSettings.defaultRenderPipeline` and `QualitySettings.renderPipeline`. ADR 0003 intentionally allowed this during early hand-bootstrap, but M8 is now a device-readiness boundary and `BuildAndroidDevelopment()` itself calls `ApplyProjectSetup()`.

Consequences:
- a first semantic/editor/build run can mutate the checkout;
- build provenance can depend on generated project state not present at the planned source SHA;
- a clean clone is not yet the exact canonical project state that is being certified.

Required disposition:
- on a real licensed Unity import, determine the authoritative generated settings/assets;
- prefer committing stable project-generated canonical settings needed for reproducible clean checkout;
- alternatively, if generation remains intentional, prove deterministic generation and explicitly bind generated-state hash/diff into certification;
- never hand-fabricate Unity serialized assets merely to satisfy this task without the editor;
- ensure final build/certification starts from or returns to a clean, explainable source state.

Severity: MEDIUM-HIGH; it is a reproducibility/certification problem, not proof that current visuals are wrong.

## 5. Platform findings requiring reproduction before code changes

### P1 — Android denial classification can regress across process restart

The native bridge returns Granted or NotDetermined on API 29+ when permission is absent. C# refines NotDetermined to Denied using `shouldShowRequestRationale` and a process-local `_completedRequestWithoutGrant` flag.

After process restart following denial, that in-memory flag is lost. On platform states where the rationale signal is also false, a prior denial can look fresh/NotDetermined again and the request path may enter its bounded polling window.

Required disposition:
- verify current Android permission semantics against official SDK behavior and a real/emulated supported target where possible;
- add an engine-testable classification seam/state table;
- reproduce denial -> process restart -> refresh/request;
- ensure no repeated prompt loop or long false request wait after a durable denial;
- do not persist a guessed permission state as authority when the OS can provide a better signal.

Status: REPRODUCE FIRST. Do not label as a confirmed platform bug until demonstrated.

### P2 — iOS query callback and provider teardown need AOT/device ownership proof

The Objective-C++ bridge uses process-global pedometer/query callback state and asynchronous handlers. The C# provider uses static pending-query/callback registration state. The M8.5 ownership contract requires old provider generations and late completions not to mutate or strand a new runtime.

Required disposition:
- audit managed delegate retention under IL2CPP/AOT;
- prove pending historical queries after Shutdown cannot create stale ownership or unresolved claims;
- prove live accumulator reads/writes are safe for the native callback/poll threading model;
- add source-level lifetime tests where possible;
- certify on real iOS only with macOS/Xcode/signing/device; otherwise leave the native/AOT tier UNVERIFIED.

Status: REPRODUCE/CERTIFY FIRST. Avoid speculative native rewrites without evidence.

## 6. Secondary integrity findings

### M1 — Vitality spend reason codes are not validated symmetrically

`VitalityLedger.Credit` rejects an empty reason code; `TrySpend` does not, despite the ledger contract stating every mutation carries an auditable reason. Existing production callers appear to supply reason codes, so this is defense in depth rather than an observed player-facing failure.

Disposition: add the missing invariant and regression unless a documented reason exists for anonymous spends.

### M2 — Reward numeric addition is unchecked

`RewardApplier.GrantResource` performs unchecked `current += amount`; positive overflow can wrap negative and then clamp to zero. Region score addition likewise can overflow after the action amount is clamped to int.

Authored content currently uses small values, so this is not a normal catalog exploit path. The campaign should nonetheless make canonical numeric mutation saturating/checked or prove a tighter upstream bound and test boundary values.

### M3 — Some procedural presentation paths assume Shader.Find succeeds

Several world actors construct a Material after only URP/Standard shader lookup. Stripped/mobile builds can expose missing shader variants differently from editor static inspection.

Disposition: include in semantic/build/device sweep; fix only if reproduced or if a cheap null-safe fallback is unambiguously correct.

## 7. What did not produce a new blocker

- Asset/meta pairing is complete.
- Android manifest remains minimal: activity recognition only, optional step-counter feature, no mandatory location permission.
- Save M8.7 null-region/transaction repairs are present.
- Git first-push/race guards include the M8.7 exact-ref distinction.
- Activity delivery ownership and rollback ordering remain internally coherent in source review.
- Builder and Explore presentation continue to project the same canonical region state.
- No new feature expansion is necessary to close the findings above.

## 8. Why M8.8 outranks M9

A closed playtest is useful only when the project can be imported/compiled reproducibly and save compatibility cannot falsely declare unsupported state valid. H1 and H2 are confirmed source defects at those exact boundaries. H3 explains why H1 escaped prior certification, and H4 weakens source-to-build reproducibility.

M8.8 therefore has higher priority than M9. After M8.8:
- if semantic editor/build/device gates are green, advance to M9 Closed Playtest Readiness;
- if real editor/device evidence exposes a blocker, the next campaign targets that measured blocker;
- do not expand to Region 2 merely because headless tests are green.

## 9. External evidence boundaries

A planner cannot convert absent prerequisites into PASS. The executor may run the following only when genuinely available:
- licensed Unity 6000.3.4f1 import/compile;
- EditMode/PlayMode;
- Android Build Support + IL2CPP/ARM64;
- Android selected-target lifecycle and physical step counter;
- touch/safe-area UX and measured performance/battery/thermal;
- macOS/Xcode/signing/device iOS.

Unavailable tiers remain `UNVERIFIED — <exact blocker>`. Do not repeatedly retry an unchanged external prerequisite.

## 10. Campaign exit condition

M8.8 may be marked COMPLETE when every locally executable H1-H4 and M1-M2 requirement is closed with regression evidence; P1/P2 are either genuinely reproduced/certified/fixed or explicitly left at the correct external evidence tier; all final available gates are green; documentation reflects actual evidence; and no new Critical/High regression remains.

Completion of locally executable work does not authorize falsely marking physical/editor tiers PASS.
