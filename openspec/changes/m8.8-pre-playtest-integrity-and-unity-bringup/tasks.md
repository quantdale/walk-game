# M8.8 Tasks — Pre-Playtest Integrity & Unity Bring-Up Closure

Status: ACTIVE
Planned-From: main@cf260d04fefbb2d5e7da265de5ae03a9aa768a0a
Autonomous work budget: up to 12 hours
Executor rule: continue across every legitimately executable workstream. Do not stop after the first fix. Do not manufacture unrelated work to consume wall-clock time.

## 0. Identity, reconciliation and single-writer setup

- [ ] Run the repository identity guard and stop on mismatch.
- [ ] Read AGENTS.md, .agent/PLANNER_HANDOFF.md, .agent/EXECUTION_PROMPT.md, this entire M8.8 OpenSpec, IMPLEMENTATION_STATUS, MASTER_PLAN, ROADMAP, TECHNICAL_ARCHITECTURE, DATA_MODEL, TESTING_AND_PERFORMANCE, AGENT_EXECUTION_GUIDE, MOBILE_ACTIVITY_INTEGRATION, ACTIVITY_REWARD_SYSTEM, PRIVACY_SAFETY_ANTI_CHEAT and ADR 0003/0005/0007/0009/0010/0011.
- [ ] Fetch origin and record current origin/main, HEAD, branch, worktree, recent commits and open PRs/issues.
- [ ] Compare current main to planned-from `cf260d04fefbb2d5e7da265de5ae03a9aa768a0a`; inspect every intervening commit if main advanced.
- [ ] Create a dedicated M8.8 implementation branch/worktree from reconciled current main.
- [ ] Acquire the repository writer lease before mutation.
- [ ] Record start SHA, branch/worktree and lease identity.
- [ ] Prove no sibling-repository contamination.

## 1. Fresh baseline and environment inventory

Run fresh and record exact results:
- [ ] repository identity guard;
- [ ] `dotnet test verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj`;
- [ ] `scripts/verify-domain.ps1`;
- [ ] `scripts/verify-unity-static.ps1`;
- [ ] `scripts/verify-release-hygiene.ps1`;
- [ ] `scripts/Test-AgentGuards.ps1`;
- [ ] `scripts/Test-CertificationScripts.ps1`;
- [ ] `git diff --check`.
- [ ] Inventory exact Unity editor/license state, Android Build Support, .NET, JDK/SDK/NDK/adb, connected devices/emulators, step-counter capability, macOS/Xcode/signing/iOS availability.
- [ ] Do not reuse M8.7 counts as current evidence.

## 2. H1 — reproduce and fix Editor semantic compile defect

- [ ] Inspect `Assets/WalkGame/Editor/WalkGameEditorTools.cs` and record the unresolved namespace references before changing code.
- [ ] Add the correct Unity API namespace/qualification for `GraphicsSettings`.
- [ ] Add the correct Unity API namespace/qualification for `IPostprocessBuildWithReport`.
- [ ] Keep the source change minimal; do not hide the issue behind reflection or dynamic lookup.
- [ ] If pinned Unity is available, run semantic import/compile immediately after the fix.
- [ ] Sweep all compiler output for additional errors in Editor, App, UI, World, Android and iOS-guarded assemblies.
- [ ] Fix every newly exposed Critical/High compile/import blocker in scope.
- [ ] Add a regression/certification guard so this exact false-green class cannot silently recur.

## 3. H3 — add a dedicated semantic Unity compile/import gate

- [ ] Implement a dedicated semantic compile/import wrapper, preferably `scripts/verify-unity-compile.ps1`.
- [ ] Reuse exact Unity `6000.3.4f1` identity preflight.
- [ ] Remove/uniquely identify stale prior evidence before launch.
- [ ] Capture source SHA and pre-run dirty state.
- [ ] Preserve a full current-run editor/import/compile log.
- [ ] Fail on editor launch/exit failure.
- [ ] Fail on compiler errors/import failure even if the Unity process exit is ambiguous.
- [ ] Prove the run reached a completed semantic import/compile state.
- [ ] Capture post-run dirty/untracked state and fail on unexplained canonical project mutation.
- [ ] Emit machine-readable evidence with source/editor/time/result/log identity.
- [ ] Add engine-free fixture tests for stale/missing/error/success evidence semantics.
- [ ] Add the gate to `scripts/README.md` and the local certification order.
- [ ] Update CI only if a legitimate licensed Unity runner exists; do not invent one.

## 4. H2 — repair SaveMigrator contract

Reproduce first:
- [ ] add a test for current schema v1 success;
- [ ] add explicit schema 0 input;
- [ ] add negative schema input;
- [ ] preserve newer/forward schema rejection coverage;
- [ ] add a progress-guard test or equivalent proof that a missing/non-advancing migration step cannot succeed or loop forever.

Implement:
- [ ] define/document the minimum-supported schema;
- [ ] make success require `schemaVersion == Current`;
- [ ] reject unsupported lower schema rather than silently coercing it;
- [ ] require each future sequential migration step to advance exactly as designed;
- [ ] return a precise error for missing/invalid migration path;
- [ ] preserve current-schema payload unchanged aside from existing load validation;
- [ ] preserve forward-schema refusal and backup/quarantine semantics.

Integration:
- [ ] drive lower-schema material through the real repository load policy and prove it fails closed rather than auto-creating a replacement profile;
- [ ] prove valid v1 save/load/rollback remains green;
- [ ] update DATA_MODEL/TECHNICAL_ARCHITECTURE/ADR only if the documented migration contract changes materially.

## 5. H4 — clean-checkout Unity project-state / URP reproducibility

This lane requires a genuine licensed Unity editor for generated assets.

- [ ] Confirm tracked tree does not already contain the pipeline asset/project settings created by `ApplyProjectSetup`.
- [ ] Starting from a clean worktree, run semantic import and project setup with pinned Unity if available.
- [ ] Capture the exact first-run Git diff including untracked files.
- [ ] Classify generated files into canonical trackable project state, cache/generated ignored state and machine-specific state.
- [ ] Prefer tracking stable editor-generated canonical URP/project settings needed for reproducibility.
- [ ] Run setup a second time and prove idempotence/no unexplained new diff.
- [ ] Run semantic compile from the materialized clean canonical state.
- [ ] Ensure Android build provenance binds to the materialized source state.
- [ ] Never hand-write opaque Unity serialized assets without a real editor.
- [ ] If Unity is unavailable, record `UNVERIFIED — <exact license/editor blocker>` once and continue other lanes.

## 6. P1 — Android motion denial across process restart

- [ ] Re-read current official Android permission semantics for the supported API range before changing behavior.
- [ ] Document a state table: fresh, granted, denied, denial + process restart, Settings grant, Settings revoke, unavailable, and permanent/no-more-dialog equivalent when the API exposes one.
- [ ] Factor permission refinement so the state table is deterministically headless-testable without JNI where practical.
- [ ] Add a denial -> process restart -> refresh/request regression.
- [ ] Prove repeated user actions cannot stack permission prompts.
- [ ] Prove a misclassified prior denial cannot cause an unnecessarily long request poll loop.
- [ ] Verify actual target behavior on Android when a suitable emulator/device exists.
- [ ] If a real defect reproduces, implement the narrowest platform/provider fix and rerun all movement/permission tests.
- [ ] If behavior is inherently ambiguous by Android API design, document safe retry UX rather than inventing certainty.
- [ ] Do not mark physical permission behavior PASS from mocks alone.

## 7. P2 — iOS callback/provider lifetime and AOT ownership

Source/headless lane:
- [ ] Trace native query callback pointer, `CMPedometer`, query queue, live accumulators and session stop.
- [ ] Trace C# static callback registration, pending-query map, provider generation and `Shutdown()`.
- [ ] Verify managed delegate lifetime/retention strategy is explicit and safe for IL2CPP/AOT.
- [ ] Define expected behavior for a historical query completing after provider Shutdown.
- [ ] Define expected behavior across GameHost recomposition/new provider generation.
- [ ] Add deterministic lifetime/late-result tests where they can be expressed without pretending to execute CoreMotion.
- [ ] Verify no late callback can credit/acknowledge movement directly; canonical mutation stays behind existing transaction ownership.

Real iOS lane, only with prerequisites:
- [ ] generate Xcode project;
- [ ] inspect/link CoreMotion and plist postprocessing;
- [ ] build/sign/install;
- [ ] exercise first permission, denial, Settings change if possible, historical query, live session, background/resume, shutdown/relaunch and late-result ownership;
- [ ] preserve logs/build/device evidence.

If macOS/Xcode/signing/device are unavailable:
- [ ] record exact UNVERIFIED blocker and do not invent an iOS PASS.

## 8. M1/M2 — secondary canonical integrity closure

### Vitality reason-code invariant
- [ ] add regression proving an empty/null spend reason cannot mutate balance/history;
- [ ] make `VitalityLedger.TrySpend` enforce the same audit-identity requirement as Credit;
- [ ] sweep all production spend callers and confirm valid reason codes already exist.

### Reward overflow invariant
- [ ] add boundary tests for resource grant overflow/underflow;
- [ ] add boundary tests for each region score mutation or common helper;
- [ ] replace unchecked wraparound with documented checked/saturating behavior;
- [ ] prove normal authored Ashfall rewards are unchanged.

### Presentation null-shader disposition
- [ ] inspect every `Shader.Find` -> `new Material` path;
- [ ] add a safe fallback/null guard when unambiguously correct, otherwise cover it in real semantic/build/device verification;
- [ ] do not redesign visuals.

## 9. Whole-repository regression sweep after fixes

- [ ] Re-inventory all tracked paths and changed-file effects.
- [ ] Search all C# for TODO/FIXME/HACK/XXX/NotImplementedException introduced or exposed by the campaign.
- [ ] Recheck all asmdefs and Editor/API namespace dependencies.
- [ ] Recheck save deserialize/migrate/validate/load/rollback boundaries.
- [ ] Recheck all `SaveMigrator` callers/tests.
- [ ] Recheck canonical resource/Vitality/score mutation paths.
- [ ] Recheck Android permission/provider startup/shutdown if changed.
- [ ] Recheck iOS callback/provider ownership if changed.
- [ ] Recheck activity exactly-once/dedup/cursor invariants even when not directly edited.
- [ ] Recheck UI/world event subscription and runtime teardown if touched.
- [ ] Recheck all changed .meta/scene/project settings structurally.
- [ ] Recheck release privacy/logging and no-GPS passive step policy.
- [ ] Recheck sibling-repository identity boundary.
- [ ] Fix all newly discovered Critical/High regressions before closure.

## 10. Genuine editor/build/device certification — only when prerequisites exist

### Unity
- [ ] semantic compile/import;
- [ ] EditMode with fresh validated XML/log;
- [ ] PlayMode with fresh validated XML/log;
- [ ] first-import/setup idempotence.

### Android
- [ ] IL2CPP ARM64 development build;
- [ ] build provenance manifest with source/toolchain/APK SHA-256;
- [ ] exact selected-target lifecycle smoke;
- [ ] permission matrix on target;
- [ ] genuine step-counter exactly-once cases if hardware exists;
- [ ] touch/safe-area/Builder/Explore/save-recovery UX;
- [ ] measured FPS/frame time/GC/memory;
- [ ] battery/thermal sample.

### iOS
- [ ] only genuine macOS/Xcode/signing/device evidence as described in section 7.

Unavailable tiers remain explicitly UNVERIFIED.

## 11. Mandatory final gates

From final source run every available gate fresh:
- [ ] repository identity guard;
- [ ] `dotnet test verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj`;
- [ ] `scripts/verify-domain.ps1`;
- [ ] `scripts/verify-unity-static.ps1`;
- [ ] `scripts/verify-release-hygiene.ps1`;
- [ ] `scripts/Test-AgentGuards.ps1`;
- [ ] `scripts/Test-CertificationScripts.ps1`;
- [ ] new semantic compile-wrapper fixture suite;
- [ ] migration/ledger/reward focused suites;
- [ ] semantic Unity compile if available;
- [ ] EditMode/PlayMode if available;
- [ ] Android build/smoke/device if available;
- [ ] iOS if available;
- [ ] `git diff --check`.

No unavailable tier may be marked PASS.

## 12. Documentation and evidence closure

- [ ] Update `docs/IMPLEMENTATION_STATUS.md` with M8.8 findings, fixes and exact fresh evidence.
- [ ] Update `docs/TESTING_AND_PERFORMANCE.md` with the semantic-compile gate and any real measurements.
- [ ] Update `scripts/README.md` with exact verification order.
- [ ] Update ADR 0003 if first-import/project-state policy changes.
- [ ] Update save architecture/data docs if migration policy wording changes.
- [ ] Update mobile activity docs only if Android/iOS behavior actually changes.
- [ ] Mark each M8.8 finding DONE, UNVERIFIED or explicitly deferred with reason; do not use ambiguous "probably fixed".
- [ ] Update this OpenSpec status/evidence footer.
- [ ] Append a detailed executor report to `.agent/EXECUTION_PROMPT.md`.

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
- [ ] Do not stop after one successful fix while later executable lanes remain.
- [ ] Do not spend hours retrying unchanged license/UAC/hardware/signing blockers.
- [ ] Reproduce before platform-specific changes whenever feasible.
- [ ] Do not add unrelated features to consume time.
- [ ] If every legitimate executable requirement completes early, finish early with truthful evidence.
- [ ] If the budget ends with productive in-scope work remaining, leave an exact continuation point, failing/passing evidence and next command.

## 14. Completion / Git handoff

- [ ] Fetch origin before final integration/push.
- [ ] Run repository remote-advance guard.
- [ ] Reconcile any competing commits deliberately; never force.
- [ ] Set M8.8 Status COMPLETE only when all locally executable requirements are done and external tiers are accurately classified.
- [ ] Change `.agent/EXECUTION_PROMPT.md` ACTIVE -> COMPLETE and append the full executor report.
- [ ] Commit with a detailed full-session report including:
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
- [ ] Push the implementation branch normally.
- [ ] Never force-push or delete remote refs.

## 15. Next-campaign gate

Recommend M9 Closed Playtest Readiness only if:
- [ ] H1-H4 are closed at every locally executable tier;
- [ ] M1-M2 are closed/dispositioned;
- [ ] no new Critical/High defect remains;
- [ ] any available semantic editor/build/device gates are green or their blocker is genuinely external;
- [ ] no real measured blocker requires a narrower campaign.

Do not recommend Region 2 from this campaign.
