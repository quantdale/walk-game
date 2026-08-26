# Device Readiness Specification — M8.6

## 1. Scope

This specification defines the normative requirements for certifying the existing one-region Walk Game vertical slice through Unity editor/runtime and mobile-device evidence. It does not authorize unrelated product expansion.

Keywords **MUST**, **MUST NOT**, **SHOULD**, and **MAY** are normative.

## 2. Repository / campaign safety

### R1 — Exact repository identity
The executor MUST prove the checkout is `quantdale/walk-game` before mutation. A mismatch MUST stop the campaign.

### R2 — Single writer
The executor MUST follow one-writer/one-branch/one-worktree and writer-lock requirements from `AGENTS.md`.

### R3 — Reconciliation
The executor MUST fetch and inspect all main-branch changes after planned SHA `3bbdbcca11fb20a6680dbb96e808b9df2cca31f3` before implementation. Equivalent landed fixes MUST be preserved rather than duplicated or reverted.

### R4 — No destructive integration
The campaign MUST NOT force-push, silently overwrite competing work, or bypass remote-advance protection.

## 3. Evidence truthfulness

### E1 — Fresh evidence only
A current campaign PASS MUST come from a command/run performed during this campaign against the reconciled source SHA. Historical M8.5 evidence MAY be cited as baseline context but MUST NOT be re-labeled fresh.

### E2 — Artifact-backed PASS
Where a gate defines a result/log/build/screenshot/trace artifact, the gate MUST NOT be marked PASS unless the artifact exists and is usable.

### E3 — Tier separation
AUTOMATED evidence MUST NOT be described as EDITOR or DEVICE evidence. Emulator lifecycle results MUST NOT be described as physical step-sensor certification.

### E4 — Exact identity
Editor/build/device evidence MUST record enough identity to reproduce the run: source SHA, editor version, build artifact identity, and device identity where applicable.

### E5 — Honest blockers
Unavailable Unity license, Build Support, administrator elevation, physical hardware, macOS/Xcode or signing MUST remain `UNVERIFIED — <specific blocker>`. The executor MUST NOT bypass those prerequisites.

## 4. Baseline gates

### B1 — Standalone correctness
Before Unity/device mutation, the executor MUST run the current standalone domain suite and record exact pass/fail count.

### B2 — Static project integrity
The executor MUST run the Unity static verifier and release-hygiene/privacy verifier.

### B3 — Agent/repository guards
Identity and available guard suites MUST pass before integration.

### B4 — Baseline failure handling
A new baseline failure on the reconciled source MUST be investigated before proceeding to downstream certification. The executor MUST distinguish environment failure from source regression.

## 5. Unity first-import / compile

### U1 — Pinned editor
EDITOR evidence MUST use Unity `6000.3.4f1` unless an explicit repository change first updates the pin and documents why.

### U2 — Legitimate license
A semantic editor compile MUST use a valid licensed/entitled session. No activation bypass is allowed.

### U3 — Full assembly compile
The imported project MUST compile every assembly relevant to the current project configuration, including Editor/App/UI/World and platform-adapter source conditions that Unity evaluates for the active target.

### U4 — Zero compiler errors
The compile gate MUST fail on any C# compiler error, asmdef resolution error, package-resolution error, or import exception that prevents normal editor/runtime operation.

### U5 — Predicted editor-reference check
The executor MUST explicitly verify the `WalkGameEditorTools.cs` references to `GraphicsSettings` and `IPostprocessBuildWithReport`. If Unity reproduces missing-namespace errors, the smallest correct import/qualification fix MUST be made and the editor gate rerun.

### U6 — Regression guard
For every compile defect fixed, the executor SHOULD add the lowest-cost deterministic guard that can catch the defect class without pretending to replace Unity compilation.

## 6. EditMode / PlayMode certification

### T1 — EditMode fail-closed runner
The EditMode verifier MUST require:

1. Unity process exit code 0;
2. non-empty result XML;
3. parseable completed test result;
4. zero test failures;
5. editor log artifact.

A missing/invalid result file MUST fail the gate even if Unity returns 0.

### T2 — PlayMode fail-closed runner
The PlayMode verifier MUST provide equivalent completed-result semantics, not merely process success.

### T3 — Current RuntimeCertificationTests
The current PlayMode certification suite MUST pass after compile/import repair unless a test is proven invalid. Tests MUST NOT be deleted/weakened simply to obtain green evidence.

### T4 — Mandatory runtime behaviors
PlayMode evidence MUST cover at least:

- Bootstrap + EventSystem composition;
- Ashfall canonical hydration;
- fake activity -> accepted steps -> Vitality -> restoration;
- deterministic offline production;
- placement move/rotation -> persist -> reload;
- Builder/Explore canonical transform parity;
- denied motion permission remaining non-blocking;
- interrupted Expedition marker recovery;
- corrupt-save fail-closed boot preserving evidence bytes;
- explicit start-over quarantining failed-save evidence and recomposing gameplay.

### T5 — Unity-only defect regressions
A defect that exists only because of Unity lifecycle, scene composition, serialized assets, editor APIs or platform integration SHOULD receive EditMode/PlayMode regression coverage where feasible.

## 7. Certification tooling requirements

### C1 — Android target selection
Every adb command in Android smoke/device automation MUST target one exact serial. If no serial is supplied, the script MUST fail unless exactly one authorized/online target is eligible.

### C2 — Target metadata
Android artifact summary MUST record at least serial/test alias, manufacturer/model, Android release/API level, ABI, source SHA, package ID, APK SHA-256, and step-counter feature availability.

### C3 — Failure artifacts
Smoke automation SHOULD preserve logs/summary on failure whenever technically possible.

### C4 — Emulator labeling
An emulator without a genuine step-counter MUST be labeled lifecycle-only and MUST NOT satisfy physical movement requirements.

### C5 — APK identity
Every APK used for certification MUST be cryptographically identified (SHA-256 or stronger) and linked to the source SHA/build log.

## 8. Android build

### A1 — Supported backend/architecture
The certification development build MUST preserve IL2CPP + ARM64 and current package identity unless a documented build-system defect requires a deliberate change.

### A2 — SDK policy
The build MUST preserve minSdk 26 and targetSdk 35 unless current tooling makes a change necessary and the executor documents compatibility implications.

### A3 — Build artifact
PASS requires `Builds/Android/WalkGame-dev.apk` (or a deliberately renamed equivalent), successful Unity build report/log, APK hash, size and source SHA.

### A4 — Build Support blocker
Missing Android Build Support is an external blocker only after the executor has confirmed the exact module state and whether legitimate installation is possible in the environment. UAC/elevation MUST NOT be bypassed.

## 9. Android lifecycle smoke

### L1 — Clean launch
A clean install + cleared data MUST cold-launch Bootstrap without fatal exceptions.

### L2 — Background/resume
The app MUST survive background/resume without process corruption or state reset.

### L3 — Force-stop/relaunch
The app MUST relaunch after force-stop and recover persisted state cleanly.

### L4 — Fatal sweep
The smoke session MUST be free of `FATAL EXCEPTION`, app ANR, or equivalent fatal-process evidence.

### L5 — Aspect/orientation
At least one supported aspect/orientation change SHOULD be attempted and its result recorded; unsupported device policy may downgrade this single check without converting a crash into PASS.

## 10. Physical Android movement certification

These requirements apply only to a physical target reporting `android.hardware.sensor.stepcounter`.

### P1 — Contextual motion permission
Fresh install MUST show the motion permission flow only when movement rewards are enabled/requested, and denial MUST remain a normal playable state.

### P2 — Later grant recovery
Granting motion permission later in OS Settings MUST allow passive movement to resume without reinstall or profile reset.

### P3 — Baseline semantics
Initial/reboot-reset counter baselines MUST NOT produce negative or implausibly huge reward.

### P4 — Known walk
A known walking sample (recommended >=200 physical steps) with screen off MUST produce one bounded accepted-step/Vitality delta and MUST NOT double-pay on subsequent reconciliation.

### P5 — Background walk
Movement accumulated while the app is backgrounded MUST be credited once after resume.

### P6 — Process death
Movement spanning force-stop/process death MUST converge exactly once after relaunch; interrupted-session recovery MUST not permanently suppress passive credit.

### P7 — Device reboot
A phone reboot/counter reset MUST rebaseline safely and subsequent movement MUST credit once.

### P8 — Expedition completion
A real Walk Expedition MUST complete through the shared transaction path, pay once after durable persistence, and suppress overlapping passive double-credit.

### P9 — Location denied fallback
With location denied/unavailable, base step rewards MUST remain usable; no GPS permission loop may block passive walking.

### P10 — Duplicate-credit probe
Repeated reconciliation/relaunch around the same movement window MUST show no duplicate credited transaction/reason identity for that physical movement.

### P11 — Exactly-once failure severity
Any reproducible duplicate credit or permanent movement loss caused by repository logic is a release-blocking High/Critical defect. The executor MUST preserve evidence, repair root cause, add deterministic regression coverage, and rerun affected device scenarios.

## 11. Player-visible mobile runtime

### V1 — Touch interaction
Buttons, project interactions, placement controls and Explore joystick MUST be interactable on touch hardware.

### V2 — Safe area/readability
Core HUD/status/permission/project content MUST remain legible and reachable within the device safe area at the tested resolution/aspect ratio.

### V3 — Builder/Explore parity
A moved/rotated building MUST appear at the same canonical transform when entering Explore mode on the device runtime.

### V4 — Truthful failure UX
Permission denial, failed/unavailable sensor, and save-recovery surfaces MUST not claim success or instruct the player toward an impossible action.

### V5 — Feedback settings
Audio/effects/haptics settings MUST visibly/audibly respect canonical persisted values where the device supports the feedback channel.

## 12. Performance / battery / thermal

### F1 — Device record
Performance evidence MUST name the device/OS/chipset or other available hardware class data.

### F2 — Separate modes
Builder and Explore MUST be measured separately. A single aggregate FPS claim is insufficient.

### F3 — Initial frame target
The repository's current experiential target remains >=30 FPS on selected low/mid supported hardware and 60 FPS on stronger hardware where practical. Actual measurements MUST be recorded even when targets are missed.

### F4 — GC / memory
The executor SHOULD capture GC allocation/collection and memory evidence using the best available Unity/platform tools.

### F5 — Battery / thermal
At least one sustained mobile session SHOULD record battery delta and thermal state. A thermal shutdown, ANR, or severe sustained degradation is a blocking result until understood.

### F6 — Measure-before-optimize
Performance code/content changes MUST include before/after evidence on the same device and scenario. No unmeasured optimization claim is allowed.

## 13. iOS

### I1 — Environment requirement
iOS EDITOR/DEVICE evidence requires macOS, Xcode, valid signing/provisioning and a real supported device where a device case is claimed.

### I2 — Plist permission string
A generated Xcode project MUST contain `NSMotionUsageDescription` with player-appropriate wording.

### I3 — CoreMotion cases
When environment exists, iOS checklist I1–I7 in `docs/DEVICE_CERTIFICATION_CHECKLISTS.md` MUST be run with artifacts before iOS M8 is claimed.

### I4 — No simulated PASS
Without the iOS environment, source/static review MAY be performed but EDITOR/DEVICE states MUST remain UNVERIFIED.

## 14. Privacy / release invariants

### Q1 — Passive steps without GPS
No change in this campaign may make passive movement depend on GPS/location permission.

### Q2 — Sensitive logging
Certification instrumentation MUST NOT introduce raw GPS route logging, raw sensor dumps beyond existing privacy rules, or local save-path leakage into release logs.

### Q3 — Release hygiene
The release-hygiene/privacy verifier MUST remain green after all changes.

## 15. Completion requirements

### X1 — No unresolved campaign blocker
No discovered Critical/High defect may remain open at campaign completion unless it is proven external to repository code and documented as such.

### X2 — Final full rerun
All locally available baseline/editor/build/device gates affected by the changes MUST be rerun from the final source state.

### X3 — Status documentation
`docs/IMPLEMENTATION_STATUS.md` MUST record exact fresh evidence, device/build identities and remaining UNVERIFIED tiers.

### X4 — OpenSpec closure
Every completed task MUST have evidence. Unexecutable hardware/platform tasks MUST be marked blocked/unverified, not checked as passed.

### X5 — Detailed executor report
`.agent/EXECUTION_PROMPT.md` MUST be converted from ACTIVE to COMPLETE (or BLOCKED only if genuinely unable to progress) with start SHA, reconciled base, branch/worktree/lease, final SHA(s), defects, fixes, commands, test counts, editor/build/device artifacts, performance data, blockers and next recommendation.

### X6 — Safe push
Before push/integration, remote advancement MUST be checked and any collision deliberately reconciled. No force push.
