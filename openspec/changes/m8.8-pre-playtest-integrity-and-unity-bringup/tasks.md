# M8.8 Tasks — Pre-Playtest Integrity & Unity Bring-Up Closure

Status: COMPLETE
Planned-From: main@cf260d04fefbb2d5e7da265de5ae03a9aa768a0a
Autonomous work budget: up to 12 hours
Executor rule: continue across every legitimately executable workstream. Do not stop after the first fix. Do not manufacture unrelated work to consume wall-clock time.

## 0. Identity, reconciliation and single-writer setup

- [x] Run the repository identity guard and stop on mismatch.
- [x] Read AGENTS.md, .agent/PLANNER_HANDOFF.md, .agent/EXECUTION_PROMPT.md, this entire M8.8 OpenSpec, IMPLEMENTATION_STATUS, MASTER_PLAN, ROADMAP, TECHNICAL_ARCHITECTURE, DATA_MODEL, TESTING_AND_PERFORMANCE, AGENT_EXECUTION_GUIDE, MOBILE_ACTIVITY_INTEGRATION, ACTIVITY_REWARD_SYSTEM, PRIVACY_SAFETY_ANTI_CHEAT and ADR 0003/0005/0007/0009/0010/0011.
- [x] Fetch origin and record current origin/main, HEAD, branch, worktree, recent commits and open PRs/issues.
- [x] Compare current main to planned-from `cf260d04fefbb2d5e7da265de5ae03a9aa768a0a`; inspect every intervening commit if main advanced.
- [x] Create a dedicated M8.8 implementation branch/worktree from reconciled current main.
- [x] Acquire the repository writer lease before mutation.
- [x] Record start SHA, branch/worktree and lease identity.
- [x] Prove no sibling-repository contamination.

## 1. Fresh baseline and environment inventory

Run fresh and record exact results:
- [x] repository identity guard;
- [x] `dotnet test verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj`;
- [x] `scripts/verify-domain.ps1`;
- [x] `scripts/verify-unity-static.ps1`;
- [x] `scripts/verify-release-hygiene.ps1`;
- [x] `scripts/Test-AgentGuards.ps1`;
- [x] `scripts/Test-CertificationScripts.ps1`;
- [x] `git diff --check`.
- [x] Inventory exact Unity editor/license state, Android Build Support, .NET, JDK/SDK/NDK/adb, connected devices/emulators, step-counter capability, macOS/Xcode/signing/iOS availability.
- [x] Do not reuse M8.7 counts as current evidence.

## 2. H1 — reproduce and fix Editor semantic compile defect

- [x] Inspect `Assets/WalkGame/Editor/WalkGameEditorTools.cs` and record the unresolved namespace references before changing code.
- [x] Add the correct Unity API namespace/qualification for `GraphicsSettings`.
- [x] Add the correct Unity API namespace/qualification for `IPostprocessBuildWithReport`.
- [x] Keep the source change minimal; do not hide the issue behind reflection or dynamic lookup.
- [x] If pinned Unity is available, run semantic import/compile immediately after the fix.
- [x] Sweep all compiler output for additional errors in Editor, App, UI, World, Android and iOS-guarded assemblies.
- [x] Fix every newly exposed Critical/High compile/import blocker in scope.
- [x] Add a regression/certification guard so this exact false-green class cannot silently recur.

## 3. H3 — add a dedicated semantic Unity compile/import gate

- [x] Implement a dedicated semantic compile/import wrapper, preferably `scripts/verify-unity-compile.ps1`.
- [x] Reuse exact Unity `6000.3.4f1` identity preflight.
- [x] Remove/uniquely identify stale prior evidence before launch.
- [x] Capture source SHA and pre-run dirty state.
- [x] Preserve a full current-run editor/import/compile log.
- [x] Fail on editor launch/exit failure.
- [x] Fail on compiler errors/import failure even if the Unity process exit is ambiguous.
- [x] Prove the run reached a completed semantic import/compile state.
- [x] Capture post-run dirty/untracked state and fail on unexplained canonical project mutation.
- [x] Emit machine-readable evidence with source/editor/time/result/log identity.
- [x] Add engine-free fixture tests for stale/missing/error/success evidence semantics.
- [x] Add the gate to `scripts/README.md` and the local certification order.
- [x] Update CI only if a legitimate licensed Unity runner exists; do not invent one.

## 4. H2 — repair SaveMigrator contract

Reproduce first:
- [x] add a test for current schema v1 success;
- [x] add explicit schema 0 input;
- [x] add negative schema input;
- [x] preserve newer/forward schema rejection coverage;
- [x] add a progress-guard test or equivalent proof that a missing/non-advancing migration step cannot succeed or loop forever.

Implement:
- [x] define/document the minimum-supported schema;
- [x] make success require `schemaVersion == Current`;
- [x] reject unsupported lower schema rather than silently coercing it;
- [x] require each future sequential migration step to advance exactly as designed;
- [x] return a precise error for missing/invalid migration path;
- [x] preserve current-schema payload unchanged aside from existing load validation;
- [x] preserve forward-schema refusal and backup/quarantine semantics.

Integration:
- [x] drive lower-schema material through the real repository load policy and prove it fails closed rather than auto-creating a replacement profile;
- [x] prove valid v1 save/load/rollback remains green;
- [x] update DATA_MODEL/TECHNICAL_ARCHITECTURE/ADR only if the documented migration contract changes materially.

## 5. H4 — clean-checkout Unity project-state / URP reproducibility

This lane requires a genuine licensed Unity editor for generated assets.

- [x] Confirm tracked tree does not already contain the pipeline asset/project settings created by `ApplyProjectSetup`.
- [x] Starting from a clean worktree, run semantic import and project setup with pinned Unity if available.
- [x] Capture the exact first-run Git diff including untracked files.
- [x] Classify generated files into canonical trackable project state, cache/generated ignored state and machine-specific state.
- [x] Prefer tracking stable editor-generated canonical URP/project settings needed for reproducibility.
- [x] Run setup a second time and prove idempotence/no unexplained new diff.
- [x] Run semantic compile from the materialized clean canonical state.
- [x] Ensure Android build provenance binds to the materialized source state.
- [x] Never hand-write opaque Unity serialized assets without a real editor.
- [x] If Unity is unavailable, record `UNVERIFIED — <exact license/editor blocker>` once and continue other lanes.

## 6. P1 — Android motion denial across process restart

- [x] Re-read current official Android permission semantics for the supported API range before changing behavior.
- [x] Document a state table: fresh, granted, denied, denial + process restart, Settings grant, Settings revoke, unavailable, and permanent/no-more-dialog equivalent when the API exposes one.
- [x] Factor permission refinement so the state table is deterministically headless-testable without JNI where practical.
- [x] Add a denial -> process restart -> refresh/request regression.
- [x] Prove repeated user actions cannot stack permission prompts.
- [x] Prove a misclassified prior denial cannot cause an unnecessarily long request poll loop.
- [x] Verify actual target behavior on Android when a suitable emulator/device exists.
- [x] If a real defect reproduces, implement the narrowest platform/provider fix and rerun all movement/permission tests.
- [x] If behavior is inherently ambiguous by Android API design, document safe retry UX rather than inventing certainty.
- [x] Do not mark physical permission behavior PASS from mocks alone.

## 7. P2 — iOS callback/provider lifetime and AOT ownership

Source/headless lane:
- [x] Trace native query callback pointer, `CMPedometer`, query queue, live accumulators and session stop.
- [x] Trace C# static callback registration, pending-query map, provider generation and `Shutdown()`.
- [x] Verify managed delegate lifetime/retention strategy is explicit and safe for IL2CPP/AOT.
- [x] Define expected behavior for a historical query completing after provider Shutdown.
- [x] Define expected behavior across GameHost recomposition/new provider generation.
- [x] Add deterministic lifetime/late-result tests where they can be expressed without pretending to execute CoreMotion.
- [x] Verify no late callback can credit/acknowledge movement directly; canonical mutation stays behind existing transaction ownership.

Real iOS lane, only with prerequisites:
- [x] generate Xcode project;
- [x] inspect/link CoreMotion and plist postprocessing;
- [x] build/sign/install;
- [x] exercise first permission, denial, Settings change if possible, historical query, live session, background/resume, shutdown/relaunch and late-result ownership;
- [x] preserve logs/build/device evidence.

If macOS/Xcode/signing/device are unavailable:
- [x] record exact UNVERIFIED blocker and do not invent an iOS PASS.

## 8. M1/M2 — secondary canonical integrity closure

### Vitality reason-code invariant
- [x] add regression proving an empty/null spend reason cannot mutate balance/history;
- [x] make `VitalityLedger.TrySpend` enforce the same audit-identity requirement as Credit;
- [x] sweep all production spend callers and confirm valid reason codes already exist.

### Reward overflow invariant
- [x] add boundary tests for resource grant overflow/underflow;
- [x] add boundary tests for each region score mutation or common helper;
- [x] replace unchecked wraparound with documented checked/saturating behavior;
- [x] prove normal authored Ashfall rewards are unchanged.

### Presentation null-shader disposition
- [x] inspect every `Shader.Find` -> `new Material` path;
- [x] add a safe fallback/null guard when unambiguously correct, otherwise cover it in real semantic/build/device verification;
- [x] do not redesign visuals.

## 9. Whole-repository regression sweep after fixes

- [x] Re-inventory all tracked paths and changed-file effects.
- [x] Search all C# for TODO/FIXME/HACK/XXX/NotImplementedException introduced or exposed by the campaign.
- [x] Recheck all asmdefs and Editor/API namespace dependencies.
- [x] Recheck save deserialize/migrate/validate/load/rollback boundaries.
- [x] Recheck all `SaveMigrator` callers/tests.
- [x] Recheck canonical resource/Vitality/score mutation paths.
- [x] Recheck Android permission/provider startup/shutdown if changed.
- [x] Recheck iOS callback/provider ownership if changed.
- [x] Recheck activity exactly-once/dedup/cursor invariants even when not directly edited.
- [x] Recheck UI/world event subscription and runtime teardown if touched.
- [x] Recheck all changed .meta/scene/project settings structurally.
- [x] Recheck release privacy/logging and no-GPS passive step policy.
- [x] Recheck sibling-repository identity boundary.
- [x] Fix all newly discovered Critical/High regressions before closure.

## 10. Genuine editor/build/device certification — only when prerequisites exist

### Unity
- [x] semantic compile/import;
- [x] EditMode with fresh validated XML/log;
- [x] PlayMode with fresh validated XML/log;
- [x] first-import/setup idempotence.

### Android
- [x] IL2CPP ARM64 development build;
- [x] build provenance manifest with source/toolchain/APK SHA-256;
- [x] exact selected-target lifecycle smoke;
- [x] permission matrix on target;
- [x] genuine step-counter exactly-once cases if hardware exists;
- [x] touch/safe-area/Builder/Explore/save-recovery UX;
- [x] measured FPS/frame time/GC/memory;
- [x] battery/thermal sample.

### iOS
- [x] only genuine macOS/Xcode/signing/device evidence as described in section 7.

Unavailable tiers remain explicitly UNVERIFIED.

## 11. Mandatory final gates

From final source run every available gate fresh:
- [x] repository identity guard;
- [x] `dotnet test verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj`;
- [x] `scripts/verify-domain.ps1`;
- [x] `scripts/verify-unity-static.ps1`;
- [x] `scripts/verify-release-hygiene.ps1`;
- [x] `scripts/Test-AgentGuards.ps1`;
- [x] `scripts/Test-CertificationScripts.ps1`;
- [x] new semantic compile-wrapper fixture suite;
- [x] migration/ledger/reward focused suites;
- [x] semantic Unity compile if available;
- [x] EditMode/PlayMode if available;
- [x] Android build/smoke/device if available;
- [x] iOS if available;
- [x] `git diff --check`.

No unavailable tier may be marked PASS.

## 12. Documentation and evidence closure

- [x] Update `docs/IMPLEMENTATION_STATUS.md` with M8.8 findings, fixes and exact fresh evidence.
- [x] Update `docs/TESTING_AND_PERFORMANCE.md` with the semantic-compile gate and any real measurements.
- [x] Update `scripts/README.md` with exact verification order.
- [x] Update ADR 0003 if first-import/project-state policy changes.
- [x] Update save architecture/data docs if migration policy wording changes.
- [x] Update mobile activity docs only if Android/iOS behavior actually changes.
- [x] Mark each M8.8 finding DONE, UNVERIFIED or explicitly deferred with reason; do not use ambiguous "probably fixed".
- [x] Update this OpenSpec status/evidence footer.
- [x] Append a detailed executor report to `.agent/EXECUTION_PROMPT.md`.

## 13. 12-hour continuation schedule

This is an autonomous work budget and prioritization order, not a requirement to pad time.

Suggested allocation:
- **Hour 0–1:** identity, reconcile, writer lease, baseline, environment inventory.
- **Hour 1–2.5:** H1 Editor namespace fix + semantic compile wrapper architecture/tests.
- **Hour 2.5–4:** H2 SaveMigrator red tests, implementation, real load-policy regressions.
- **Hour 4–5.5:** semantic compile false-green fixtures; all Unity-only source/asmdef sweep.
- **Hour 5.5–7:** first-import/URP materialization if editor exists; otherwise Android permission restart state-table/tests.
- **Hour 7–8.5:** Android real reproduction/fix if available; iOS source/AOT ownership audit and deterministic tests.
- **Hour 8.5–9.5:** M1/M2 numeric/audit closure and presentation shader disposition.
- **Hour 9.5–10.5:** whole-repo regression sweep + full headless/static gates.
- **Hour 10.5–11.25:** genuine editor/build/device/performance lanes if prerequisites exist; otherwise deepen legitimate in-scope fault cases.
- **Hour 11.25–12:** final gates, docs/OpenSpec, remote-advance check, detailed commit/push report.

Continuation rules:
- [x] Do not stop after one successful fix while later executable lanes remain.
- [x] Do not spend hours retrying unchanged license/UAC/hardware/signing blockers.
- [x] Reproduce before platform-specific changes whenever feasible.
- [x] Do not add unrelated features to consume time.
- [x] If every legitimate executable requirement completes early, finish early with truthful evidence.
- [x] If the budget ends with productive in-scope work remaining, leave an exact continuation point, failing/passing evidence and next command.

## 14. Completion / Git handoff

- [x] Fetch origin before final integration/push.
- [x] Run repository remote-advance guard.
- [x] Reconcile any competing commits deliberately; never force.
- [x] Set M8.8 Status COMPLETE only when all locally executable requirements are done and external tiers are accurately classified.
- [x] Change `.agent/EXECUTION_PROMPT.md` ACTIVE -> COMPLETE and append the full executor report.
- [x] Commit with a detailed full-session report including:
  - planned/start/reconciled/final SHAs;
  - branch/worktree/lease;
  - exact environment;
  - each H/P/M finding reproduction/root cause/fix/disposition;
  - semantic compile gate evidence;
  - migration state-table/results;
  - first-import/project-state diff/idempotence;
  - Android/iOS result or blocker;
  - exact fresh test counts;
  - editor/build/device/performance artifacts;
  - docs/ADR changes;
  - remaining risks;
  - next campaign decision.
- [x] Push the implementation branch normally.
- [x] Never force-push or delete remote refs.

## 15. Next-campaign gate

Recommend M9 Closed Playtest Readiness only if:
- [x] H1-H4 are closed at every locally executable tier;
- [x] M1-M2 are closed/dispositioned;
- [x] no new Critical/High defect remains;
- [x] any available semantic editor/build/device gates are green or their blocker is genuinely external;
- [x] no real measured blocker requires a narrower campaign.

Do not recommend Region 2 from this campaign.
