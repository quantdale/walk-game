# M8.6 Tasks — Unity First-Import & Device Readiness Certification

**Status:** ACTIVE FOR EXECUTION AFTER HANDOFF  
**Executor rule:** execute the entire campaign coherently. Use up to a 12-hour autonomous work budget, but never pad elapsed time with unrelated work. Fix every discovered Critical/High defect and any Medium defect necessary for truthful certification. Do not expand product scope.

## 0. Repository truth and safety — before all mutation

- [ ] Run `sh scripts/assert-repo-identity.sh` or `./scripts/Assert-RepoIdentity.ps1`; STOP on mismatch.
- [ ] Read `AGENTS.md`, `.agent/PLANNER_HANDOFF.md`, `.agent/EXECUTION_PROMPT.md`, this full OpenSpec package, `docs/IMPLEMENTATION_STATUS.md`, roadmap, architecture, data model, activity/mobile/privacy/testing docs, device checklist, ADR 0007/0008/0009/0010/0011, and any newer ADR.
- [ ] Fetch remote state and record current branch, HEAD, upstream, worktree status, `origin/main`, recent commits, open PRs/issues.
- [ ] Reconcile every commit after planned SHA `3bbdbcca11fb20a6680dbb96e808b9df2cca31f3`. Preserve equivalent landed fixes.
- [ ] Create/use one dedicated worktree and branch `agent/walk-game/m8.6-<session-id>` from current authoritative main.
- [ ] Acquire writer lease before first mutation. Record lease/session id and start SHA.
- [ ] Confirm no path/branch/SHA/content has been imported from `quantdale/simple-walk-game`.
- [ ] Record environment inventory before attempting gates:
  - OS + shell;
  - `.NET` SDK;
  - pinned Unity executable/version;
  - Unity Hub/account/license/entitlement state;
  - installed Unity modules, especially AndroidPlayer;
  - JDK/Android SDK/NDK paths and versions;
  - adb version;
  - connected adb targets and states;
  - macOS/Xcode/signing/iOS availability;
  - physical Android step-counter availability.

## 1. Fresh baseline — historical M8.5 results do not count

Run and record exact output:

- [ ] `dotnet test verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj`
- [ ] `./scripts/verify-domain.ps1` or supported shell equivalent
- [ ] `./scripts/verify-unity-static.ps1`
- [ ] `./scripts/verify-release-hygiene.ps1`
- [ ] `./scripts/Test-AgentGuards.ps1` and supported shell tier where environment permits
- [ ] repository identity guard again after environment inspection
- [ ] `git diff --check`

Rules:

- [ ] If baseline is not green, root-cause the regression/environment failure before continuing downstream.
- [ ] Record exact fresh pass count rather than copying `213/213` from M8.5.

## 2. First real Unity import / semantic compile — P0 lane

### 2.1 License/editor preflight

- [ ] Confirm Unity `6000.3.4f1` executable and package manager are present.
- [ ] Confirm a valid licensed editor session exists before claiming EDITOR evidence.
- [ ] If no license exists, capture exact Hub/licensing-client state and blocker. Do not bypass/fabricate entitlement.
- [ ] If licensed, set `UNITY_EDITOR_PATH` to the exact pinned executable.

### 2.2 Clean-enough import

- [ ] Run `scripts/setup-unity-project.ps1` or the repository-supported batch setup path.
- [ ] Capture full Unity/import log under ignored `TestResults`/`Artifacts` path.
- [ ] Fail on compiler, package resolution, asmdef, serialization/import or setup exceptions.
- [ ] Enumerate every compiler/import failure before patching so multiple root causes are not hidden by the first fix.

### 2.3 Planner-predicted compile checks

Do not preemptively change code before reproduction when Unity is available:

- [ ] Verify whether `WalkGameEditorTools.cs` fails to resolve `GraphicsSettings` because `UnityEngine.Rendering` is not imported.
- [ ] Verify whether `WalkGameEditorTools.cs` fails to resolve `IPostprocessBuildWithReport` because `UnityEditor.Build` is not imported.
- [ ] If reproduced, apply the smallest namespace qualification/import fix.
- [ ] Inspect the entire Editor assembly for equivalent editor-only missing imports/API drift.
- [ ] Inspect App/UI/World assemblies for compiler/API errors invisible to the standalone harness.
- [ ] Inspect Android and iOS platform assemblies for conditional-compilation/asmdef failures under Unity.

### 2.4 Compile-fix discipline

For every compile/import defect:

- [ ] record exact error and root cause;
- [ ] make smallest correction;
- [ ] add a static or EditMode regression if it can catch the defect class without pretending to replace Unity compile;
- [ ] rerun Unity compile/import until zero errors;
- [ ] rerun static/release gates if source/config changed.

### 2.5 Clean confirmation

- [ ] After initial green compile, perform a fresh batch reopen/reimport or equivalent clean confirmation practical in this environment.
- [ ] Store final compiler/import log and exact Unity version.

If licensed Unity cannot be obtained, leave every item requiring semantic editor evidence **UNVERIFIED — license blocker** and continue with Sections 3 and other deterministic work that does not fake Unity evidence.

## 3. Harden certification scripts so evidence fails closed

### 3.1 EditMode runner

Modify `scripts/verify-unity-editmode.ps1` as necessary:

- [ ] Require Unity exit code 0.
- [ ] Require `TestResults/editmode-results.xml` to exist and be non-empty.
- [ ] Parse/validate XML enough to prove the run completed and has zero failures.
- [ ] Require editor log artifact.
- [ ] Return nonzero if the result artifact is missing/invalid even when Unity exits 0.
- [ ] Print concise machine/operator summary with result/log paths.
- [ ] Add deterministic script-level test/fixture if practical without invoking Unity.

### 3.2 PlayMode runner parity

- [ ] Audit `verify-unity-playmode.ps1` against the same evidence semantics.
- [ ] Add result XML parse/completion/failure validation if current existence-only check can overstate a malformed/incomplete run.
- [ ] Keep EditMode and PlayMode result handling behaviorally consistent.

### 3.3 Android smoke target identity

Modify `scripts/verify-android-smoke.ps1` as necessary:

- [ ] Add explicit `-DeviceSerial` or equivalent option.
- [ ] If no serial is supplied, require exactly one authorized/online eligible adb target.
- [ ] Fail early on ambiguity, unauthorized or offline target.
- [ ] Bind every adb command to the selected serial (`-s` or equivalent).
- [ ] Record manufacturer/model, Android release, SDK, ABI, selected serial/test alias.
- [ ] Record whether `android.hardware.sensor.stepcounter` is present.
- [ ] Record APK SHA-256, APK size, package ID and source SHA.
- [ ] Preserve logcat/summary artifacts on failure whenever practical.
- [ ] Explicitly label emulator/no-step-counter runs as **lifecycle-only**.
- [ ] Keep `-KeepInstalled` behavior correct after failure/success.

### 3.4 Certification-script regression tests

- [ ] Add tests/fixture functions where feasible for device-list parsing, target selection, result XML validation and failure cases.
- [ ] Avoid introducing heavy external dependencies solely for script testing.

## 4. Unity EditMode certification

Run only with legitimate licensed editor:

- [ ] `./scripts/verify-unity-editmode.ps1`
- [ ] Verify result XML exists, parses and reports zero failures.
- [ ] Record test count, failures/skips, duration, editor version, source SHA and log/result paths.
- [ ] Triage every failure by root cause; do not delete/weaken valid tests to go green.
- [ ] Add focused regressions for defects found.
- [ ] Rerun complete EditMode gate after all fixes.

## 5. Unity PlayMode / runtime certification

Run only after clean compile + EditMode:

- [ ] `./scripts/verify-unity-playmode.ps1`
- [ ] Verify current `RuntimeCertificationTests` completes green.

Explicitly confirm evidence for:

- [ ] Bootstrap composes `GameHost` and playable rig.
- [ ] uGUI EventSystem exists and UI is interactable at runtime level.
- [ ] Ashfall hydrates all canonical building actors.
- [ ] debug passive movement credits accepted steps/Vitality once.
- [ ] restoration mutates canonical state and presentation.
- [ ] fake-clock production accrues/collects expected resource values.
- [ ] placement move/rotate persists and reloads exactly.
- [ ] Builder -> Explore projection uses identical canonical building transform.
- [ ] denied motion permission does not block Builder/Explore.
- [ ] stale Expedition marker recovers on boot and passive credit resumes.
- [ ] corrupt main+backup boots fail-closed and destruction preserves failed bytes.
- [ ] explicit start-over quarantines evidence and recomposes playable runtime.

For every Unity-only defect found:

- [ ] reproduce;
- [ ] fix root cause;
- [ ] add focused PlayMode/EditMode regression;
- [ ] rerun affected test then full PlayMode gate.

## 6. Android Build Support and release-shaped development build

### 6.1 Module/toolchain preflight

- [ ] Verify `AndroidPlayer`/Build Support for Unity `6000.3.4f1`.
- [ ] Verify editor-visible SDK/NDK/JDK paths/versions.
- [ ] If module missing and installation is legitimately possible, install through supported Unity Hub/tooling.
- [ ] If UAC/elevation/user interaction blocks installation, capture exact state and leave build/device lanes UNVERIFIED. Do not bypass elevation/security.

### 6.2 Build

- [ ] Set `UNITY_EDITOR_PATH`.
- [ ] Run `scripts/build-android-development.ps1`.
- [ ] Preserve IL2CPP + ARM64.
- [ ] Preserve package `com.quantdale.walkgame`.
- [ ] Preserve minSdk 26 / targetSdk 35 unless a deliberate toolchain compatibility change is documented.
- [ ] Require `Builds/Android/WalkGame-dev.apk` and successful Unity build log.
- [ ] Record APK SHA-256, size, source SHA, Unity version/backend/architecture/SDK levels.
- [ ] If build fails, triage all compile/link/Gradle/manifest/JNI/IL2CPP errors and fix Critical/High blockers.
- [ ] Add regression/static check for each deterministic build-config defect where practical.
- [ ] Rebuild from final source after fixes.

## 7. Android lifecycle smoke — emulator or physical target

Using hardened target-selection semantics:

- [ ] Select exact target serial.
- [ ] Run `scripts/verify-android-smoke.ps1 -DeviceSerial <serial> ...` or resulting equivalent.
- [ ] Capture install, package/version, APK hash and source SHA.
- [ ] Clean install/data clear.
- [ ] Cold launch and wait for stable Bootstrap.
- [ ] Background/resume.
- [ ] Orientation/aspect attempt where supported.
- [ ] Force-stop/relaunch.
- [ ] Sweep complete session logcat for fatal exception/ANR/process death evidence.
- [ ] Preserve summary/logcat even if a later check fails.
- [ ] If target lacks genuine step counter, mark movement tier UNVERIFIED and do not fake it.

## 8. Physical Android step-counter certification — run only on genuine hardware

Precondition:

- [ ] exactly one selected physical device;
- [ ] device reports `android.hardware.sensor.stepcounter`;
- [ ] same APK passed lifecycle smoke;
- [ ] test profile/evidence capture path is safe.

Execute and record artifact line for each case using checklist format:

### Permission
- [ ] A2 first contextual motion permission ask.
- [ ] A3 denial: game remains playable; no crash/prompt loop.
- [ ] A4 grant later in Settings: passive movement resumes without reinstall.

### Counter/exactly-once
- [ ] A5 baseline/reboot-prep behavior: no negative/huge credit.
- [ ] A6 known physical walk >=200 steps, screen off; bounded delta credits once.
- [ ] A7 background walk; credits once after resume.
- [ ] A8 force-stop/process death during movement; recovery converges once.
- [ ] A9 phone reboot; counter reset re-baselines safely.
- [ ] A10 equivalent counter-reset condition if safely reproducible on device.
- [ ] A13 duplicate-credit probe using repeated reconciliation/relaunch.

### Expedition
- [ ] A11 real Walk Expedition >=5 minutes; result pays once, passive overlap suppressed.
- [ ] A12 location denied/unavailable fallback: base steps still work, no GPS prompt loop.

For every case record:

- [ ] date/time UTC;
- [ ] device model/OS/API;
- [ ] APK hash + source SHA;
- [ ] permission state;
- [ ] relevant before/after accepted-step and Vitality values;
- [ ] screenshot/video/logcat/trace artifact path;
- [ ] PASS/FAIL with one-line reason.

If any exactly-once case fails:

- [ ] classify release-blocking High/Critical;
- [ ] preserve original evidence;
- [ ] root-cause across native bridge/provider/reconciler/transaction/persistence/lifecycle;
- [ ] extend `ActivityServiceTests`, `AndroidCounterReconciliationTests`, `SaveLoadTests` and application orchestration tests as required;
- [ ] run full headless regressions;
- [ ] rebuild/reinstall final APK;
- [ ] rerun failed device case plus adjacent lifecycle cases.

## 9. Physical Android vertical-slice UX certification

On the same or another explicitly recorded device:

- [ ] A1 first launch offline/airplane mode: fresh profile, Bootstrap visible, no fatal error.
- [ ] safe-area layout: no clipped critical controls.
- [ ] touch buttons/project rows respond correctly.
- [ ] Builder pan/zoom/selection behaves on touch.
- [ ] building move preview/confirm/cancel works and persists.
- [ ] enter Explore and use joystick/camera; return to Builder.
- [ ] moved building appears at identical location/rotation in Explore.
- [ ] project completion feedback is truthful and usable.
- [ ] producer collection/offline summary is readable and functional.
- [ ] onboarding progression is understandable and persists.
- [ ] permission banner states are neutral/actionable.
- [ ] Expedition start/finish/failure states are truthful.
- [ ] audio/effects/haptics settings apply and persist where hardware supports them.
- [ ] controlled save-recovery UX test if safe fixture tooling permits it; never corrupt a real player's profile.

Fix blocker/high usability defects that invalidate M8. Defer purely aesthetic polish with evidence and severity.

## 10. Performance / GC / memory / battery / thermal baseline

Do not optimize before capturing baseline.

### 10.1 Device profile
- [ ] Record device model, Android/SDK, chipset/GPU if obtainable, RAM, resolution/refresh.

### 10.2 Builder
- [ ] Measure sustained Builder View FPS/frame time.
- [ ] Capture representative main-thread/render timing if profiler tooling works.
- [ ] Capture GC allocation/collection indicators.
- [ ] Capture memory footprint.

### 10.3 Explore
- [ ] Measure sustained Explore View separately.
- [ ] Capture same timing/GC/memory indicators.
- [ ] Measure Builder <-> Explore transition hitch/time.

### 10.4 Battery/thermal
- [ ] Run >=20 minute combined Builder/Explore or Expedition sample where practical.
- [ ] Capture start/end battery percentage and `dumpsys batterystats`/available platform evidence.
- [ ] Capture thermal status and any throttling/ANR/crash evidence.

### 10.5 Performance decision
- [ ] Compare measured results to repository goals (>=30 FPS low/mid, 60 FPS stronger where practical).
- [ ] If a measured blocker exists, identify profiler-backed bottleneck before changing code/content.
- [ ] For every in-campaign optimization, capture before/after on same device/scenario.
- [ ] Do not claim “optimized” without measured delta.

## 11. iOS lane — conditional, never simulated

If macOS + Xcode + signing + compatible device are actually available:

- [ ] generate Xcode project from pinned Unity;
- [ ] confirm post-build `NSMotionUsageDescription` exists and wording is correct;
- [ ] build/sign/install;
- [ ] run I1 Xcode build/archive evidence;
- [ ] run I2 first CoreMotion request;
- [ ] run I3 denied permission fallback;
- [ ] run I4 historical reconciliation exactly once;
- [ ] run I5 live Expedition;
- [ ] run I6 background/resume overlapping-window dedup;
- [ ] run I7 kill-after-finish restart dedup;
- [ ] preserve artifacts and record device/build identity.

If not available:

- [ ] record exact blocker (`no macOS`, `no Xcode`, `no signing`, `no device`, etc.);
- [ ] perform only source/config readiness checks justified without an editor/device;
- [ ] keep all iOS editor/device cases UNVERIFIED.

## 12. Whole-repository regression sweep after fixes

After any implementation/certification tooling changes:

- [ ] search repository for new TODO/FIXME/NotImplementedException/dead debug bypasses introduced by the campaign;
- [ ] inspect activity completion/provider resolution/service teardown call sites if movement code changed;
- [ ] inspect persistence mutation boundaries if save code changed;
- [ ] inspect App/UI/World lifecycle subscriptions if runtime composition changed;
- [ ] inspect native bridge start/stop/permission paths if Android/iOS code changed;
- [ ] inspect all changed asmdefs/metas/scenes/config/build settings structurally;
- [ ] ensure passive steps still require no GPS/location permission;
- [ ] ensure release logging/privacy invariant remains intact.

## 13. Mandatory final gates

From final source state run every available gate:

- [ ] repository identity guard
- [ ] `dotnet test verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj`
- [ ] `verify-domain.ps1`
- [ ] `verify-unity-static.ps1`
- [ ] `verify-release-hygiene.ps1`
- [ ] `Test-AgentGuards.ps1` / supported shell tier
- [ ] Unity import/compile, if licensed
- [ ] EditMode, if licensed
- [ ] PlayMode, if licensed
- [ ] Android IL2CPP/ARM64 build, if Build Support available
- [ ] Android smoke, if target available
- [ ] physical sensor/UX/performance cases, if genuine hardware available
- [ ] iOS gates, only if genuine iOS toolchain/device available
- [ ] `git diff --check`

No unavailable tier is marked PASS.

## 14. Documentation and evidence update

- [ ] Update `docs/IMPLEMENTATION_STATUS.md` with M8.6 matrix using only fresh evidence.
- [ ] Update `docs/TESTING_AND_PERFORMANCE.md` with measured device results and hardened gate semantics.
- [ ] Update `docs/DEVICE_CERTIFICATION_CHECKLISTS.md` if target selection/evidence fields/procedures changed.
- [ ] Update `docs/TECHNICAL_ARCHITECTURE.md`, `MOBILE_ACTIVITY_INTEGRATION.md`, `ACTIVITY_REWARD_SYSTEM.md`, `PRIVACY_SAFETY_ANTI_CHEAT.md` only when behavior/contracts actually changed.
- [ ] Add ADR only for a material architecture/evidence-policy decision.
- [ ] Update OpenSpec audit/proposal/design/spec/tasks status/evidence footer honestly.

## 15. 12-hour autonomous work schedule / continuation policy

This is a prioritization budget, not permission to fabricate work. Continue to the next lane whenever the current lane is green or externally blocked.

Suggested allocation:

- **Hour 0–1:** identity, reconcile, lease, environment inventory, fresh headless/static baseline.
- **Hour 1–3:** licensed Unity import/compile triage; fix all compiler/import blockers; clean confirmation.
- **Hour 3–4.5:** harden EditMode/PlayMode and Android-smoke evidence semantics; run EditMode.
- **Hour 4.5–6:** PlayMode runtime certification; repair Unity-only defects and rerun.
- **Hour 6–7.5:** Android Build Support/toolchain, IL2CPP/ARM64 build, build defect triage.
- **Hour 7.5–9:** deterministic Android smoke and mobile vertical-slice UX checks.
- **Hour 9–10.5:** genuine step-counter exactly-once/lifecycle cases when hardware exists; otherwise iOS readiness or additional legitimate editor/device regression work.
- **Hour 10.5–11.25:** measured performance/GC/memory/battery/thermal baseline and any evidence-backed blocker fix.
- **Hour 11.25–12:** full final rerun, docs/evidence matrix, OpenSpec closure, remote-advance check, detailed commit/push report.

Continuation rules:

- [ ] Do not stop after one successful fix while later eligible lanes remain.
- [ ] Do not spend hours repeatedly retrying an external blocker with no changed precondition.
- [ ] When a lane is externally blocked, capture blocker once thoroughly and move on.
- [ ] If all legitimate locally executable work is complete before 12 hours, finish early with truthful completion; do not add unrelated features.
- [ ] If time budget ends with productive in-scope work remaining, leave exact continuation point and evidence, not a vague TODO.

## 16. Completion / handoff

- [ ] Re-run remote advancement check before integration/push.
- [ ] Reconcile any competing changes deliberately; no force push.
- [ ] Set this OpenSpec status to COMPLETE only for executed/verified requirements. Leave platform-specific blocked items explicitly UNVERIFIED rather than falsely checked.
- [ ] Change `.agent/EXECUTION_PROMPT.md` from ACTIVE to COMPLETE (or BLOCKED only if the entire campaign genuinely cannot progress) and append a detailed executor report containing:
  - planned/start/reconciled SHA;
  - branch/worktree/lease;
  - final SHA(s);
  - environment and editor/build/device identities;
  - every reproduced defect and root-cause fix;
  - exact standalone/EditMode/PlayMode counts;
  - Android APK hash/build result;
  - smoke/device case matrix + artifact paths;
  - performance/battery/thermal measurements;
  - iOS result/blocker;
  - docs/ADR changes;
  - remaining blockers/follow-ups;
  - next campaign recommendation.
- [ ] Commit with a detailed full-session report and push according to repository workflow.

### Next-campaign decision

- If M8 Android/editor/device readiness is materially green: recommend **M9 Closed Playtest Readiness / Validation**.
- If a measured mobile performance or exactly-once defect remains: recommend a focused campaign on that measured blocker.
- Do not recommend Region 2 merely because M8.6 ended.

---

## 17. Mandatory refinements from the 2026-08-27 deep re-audit

These tasks are additive to sections 0-16. Do them in the relevant lane; they are not optional polish.

### 17.1 Establish a trustworthy Unity evidence harness before claiming editor PASS

- [ ] Add a shared fail-closed preflight that proves the effective \`UNITY_EDITOR_PATH\` exists and is exactly Unity \`6000.3.4f1\`.
- [ ] Record source SHA, dirty/clean state, editor executable identity/version and invocation timestamp into an ignored machine-readable evidence artifact.
- [ ] Add an explicit batch-mode Unity import/semantic compile verifier, preserving its full editor log.
- [ ] Reproduce/disposition the \`GraphicsSettings\` / \`IPostprocessBuildWithReport\` Editor namespace prediction using the real pinned editor before calling it a confirmed compile failure.
- [ ] Sweep every Unity-only assembly after the first compile result: Editor, App, UI, World, Android and iOS guarded assemblies.
- [ ] Implement one semantic test-result validator used by both EditMode and PlayMode.
- [ ] Require a newly generated result XML, parseability, non-zero expected test population, zero failures/errors and a completed/pass run state.
- [ ] Make missing, malformed, empty, stale, cancelled/incomplete or failed XML return non-zero even if Unity itself returned 0.
- [ ] Preserve XML + editor log on failure.
- [ ] Add deterministic fixture tests for the validator where practical without Unity.

### 17.2 Make Android smoke target selection and clean-install semantics fail closed

- [ ] Add an explicit serial parameter (for example \`-Serial\`) to \`verify-android-smoke.ps1\`.
- [ ] If serial is omitted, require exactly one eligible connected target; fail on zero or multiple.
- [ ] Validate an explicit serial against \`adb devices\` before destructive/install operations.
- [ ] Centralize selected-target invocation so every adb command receives \`-s <serial>\`.
- [ ] Audit and fix direct calls that currently bypass \`Invoke-Adb\`, including \`pidof\` and every \`logcat\` capture.
- [ ] Make pre-install uninstall idempotent: package-absent is clean success, transport/device failures are real failures.
- [ ] Add a deterministic fixture/mock regression for package-absent uninstall.
- [ ] Record model, manufacturer if available, API level, ABI, serial and emulator/physical classification.
- [ ] Record \`android.hardware.sensor.stepcounter\` availability and label an emulator/no-sensor run lifecycle-only.
- [ ] Strengthen launch evidence: record foreground/resumed package/activity (or equivalent platform evidence) in addition to process-alive checks.
- [ ] Persist summary/logcat on failure where possible.
- [ ] Ensure final cleanup/uninstall disposition is written to the persisted summary, not only appended in memory after the JSON file was already emitted.

### 17.3 Bind Android build evidence to the exact source and artifact

- [ ] Extend the Android build wrapper to emit a machine-readable build manifest.
- [ ] Record source SHA + dirty state, Unity version, Android module/toolchain identities when discoverable, package id, min/target SDK, scripting backend, architecture and development/debug flags.
- [ ] Record final APK path, byte size and SHA-256.
- [ ] Refuse a PASS when Unity exits 0 but the expected current-run APK/provenance cannot be established.
- [ ] Pass or cross-check the APK hash/source SHA into smoke evidence so device results cannot accidentally refer to another build.

### 17.4 Evidence-tier truthfulness

- [ ] Treat editor compile, EditMode, PlayMode, Android build, emulator lifecycle, physical sensor, physical UX, performance/battery/thermal and iOS as distinct evidence tiers.
- [ ] Never upgrade a lower tier into a higher one by inference.
- [ ] In \`docs/IMPLEMENTATION_STATUS.md\`, name the actual artifact/target for every newly green tier.
- [ ] If hardware/tooling is unavailable, write \`UNVERIFIED — <exact blocker>\`; do not spend the remaining autonomous budget retrying an unchanged external prerequisite.

### 17.5 Conditional iOS cleanup

Only when genuine macOS/Xcode/signing/device prerequisites are present:

- [ ] Verify the postprocessor fails certification if the generated \`Info.plist\` is missing/unparseable or lacks \`NSMotionUsageDescription\`.
- [ ] Prefer structured plist editing over raw string insertion if the pinned Unity API supports it cleanly.
- [ ] Correct the stale method/class reference in \`Assets/Plugins/iOS/IOS_BUILD_REQUIREMENTS.md\`.
- [ ] Preserve generated plist/build evidence.

Otherwise:

- [ ] Leave these device/editor claims UNVERIFIED and do not expand Windows-only work into speculative iOS implementation.

### 17.6 Re-audit acceptance gates

Before M8.6 can close:

- [ ] Every R1-R9 finding in \`audit.md\` has an explicit disposition and evidence.
- [ ] Every new normative E1-E10 requirement in \`specs/device-readiness/spec.md\` is satisfied or explicitly UNVERIFIED only where an external platform prerequisite genuinely prevents execution.
- [ ] All available pre-existing gates are rerun from final source state.
- [ ] No false-green condition discovered in this re-audit remains in a certification wrapper.
- [ ] The final executor report distinguishes fixes implemented from evidence actually executed.

