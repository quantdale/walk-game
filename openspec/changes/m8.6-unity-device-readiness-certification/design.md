# M8.6 Design — Unity First-Import & Device Readiness Certification

## 1. Design principles

### D1 — Evidence is a product of the gate, not an assertion
A gate is PASS only when the command, exact source/build SHA, environment identity and required artifact exist. Process exit code alone is insufficient where the tool is expected to produce a result document, APK, log or trace.

### D2 — Preserve evidence tiers
Do not turn a static or fake-provider test into a claim about Unity, JNI, CoreMotion or physical sensors. Evidence tiers remain:

- AUTOMATED / standalone;
- EDITOR;
- DEVICE.

A lower tier can block a higher tier but cannot substitute for it.

### D3 — Compile before device certification
The dependency graph is strict:

`repo identity -> baseline gates -> Unity import/compile -> EditMode -> PlayMode -> Android build -> Android smoke -> physical sensor/lifecycle -> measured performance`.

A downstream PASS cannot erase an upstream failure.

### D4 — Fix the smallest root cause
First-import campaigns often expose stale API names, missing namespace imports, asmdef references, serialization/import errors, package incompatibilities and scene/runtime assumptions. Fix the smallest root cause, then add a regression at the lowest viable tier. Do not use a compile failure as permission for architectural churn.

### D5 — Device identity is explicit
Every adb-driven command must target one exact serial. Every device artifact summary records at least:

- serial (or privacy-safe stable test alias if publishing outside private artifacts);
- manufacturer/model;
- Android release + SDK level;
- ABI;
- app package/version/build SHA;
- whether `android.hardware.sensor.stepcounter` is reported;
- whether motion permission is granted/denied for the case;
- timestamps in UTC.

### D6 — Exactly-once failures are release blockers
Any real-device duplicate/lost movement behavior across baseline, background, process death, reboot or Expedition completion is High/Critical until understood. Preserve raw evidence, reproduce deterministically where possible, then extend `ActivityServiceTests`, `AndroidCounterReconciliationTests`, `SaveLoadTests` and/or the application orchestration suites as required by `AGENTS.md`.

### D7 — Measure before optimizing
Do not add LODs, rewrite shaders, change update cadence or lower visual quality merely because Phase 7 mentions performance. Capture frame-time/GC/memory/battery/thermal evidence first. Optimize only a measured bottleneck, then capture before/after evidence on the same device and scenario.

### D8 — External blockers stay external
The campaign must never bypass Unity licensing, OS permission models, Android SDK licensing, UAC/elevation, Apple signing, or lack of physical hardware. A blocked gate is documented with reproduction steps and exact missing prerequisite, then the executor moves to another legitimate lane.

## 2. Workstream architecture

### W1 — Repository and environment preflight

Before mutation:

1. run repository identity guard;
2. fetch current main and inspect advancement from planned SHA;
3. create/use `agent/walk-game/m8.6-<session-id>` in a dedicated worktree;
4. acquire writer lease;
5. record start SHA, branch, upstream, editor path/version, Unity license status, Android SDK/NDK/JDK paths, adb version, connected targets, installed Unity modules, and OS;
6. run standalone/static/privacy/guard baseline.

Environment probing must be read-only until identity/lease requirements are satisfied.

### W2 — Unity first-import and semantic compile

Use pinned Unity `6000.3.4f1`. Establish whether a licensed editor session is actually usable.

If licensed:

- run repository setup/import using the existing supported script/menu entry point;
- capture full editor/import log;
- fail on compiler errors, assembly-definition failures, package resolution errors or serialization/import exceptions;
- inspect every error before patching;
- specifically verify the Editor assembly, App/UI/World assemblies, Android adapter assembly and iOS adapter assembly compile in their intended platform conditions;
- reproduce the planner-predicted namespace issues in `WalkGameEditorTools.cs` before fixing if they appear;
- after fixes, close/reopen or run a clean batch import when practical to prove no incremental-cache illusion.

If not licensed:

- capture the exact entitlement/login failure;
- do not fabricate activation;
- continue with deterministic certification-harness fixes and other non-editor work permitted by this spec;
- leave EDITOR tier UNVERIFIED.

### W3 — Certification harness hardening

#### EditMode runner
Require:

- Unity exit code 0;
- result XML exists and is non-empty;
- XML is parseable;
- result summary indicates no failed tests and a completed run;
- log file exists;
- summary printed with result path and exact editor version if obtainable.

#### PlayMode runner
Bring equivalent result validation to PlayMode if its current existence check is insufficient. Keep both runners behaviorally aligned.

#### Android smoke
Add deterministic targeting:

- optional explicit `-DeviceSerial` (or equivalent);
- if omitted, exactly one connected usable target must exist;
- invoke every adb call against that serial;
- reject unauthorized/offline targets;
- record target metadata and step-counter feature availability;
- capture package dump/version and APK SHA-256;
- preserve logcat and machine-readable summary even on failure where practical;
- distinguish emulator lifecycle certification from physical step-sensor certification.

Do not make the smoke script pretend an emulator without `TYPE_STEP_COUNTER` certifies movement.

### W4 — EditMode and PlayMode certification

After compile is green:

1. run EditMode from a fresh-enough state;
2. run PlayMode `RuntimeCertificationTests`;
3. triage every failure by root cause;
4. rerun the smallest affected suite during repair;
5. rerun the complete EditMode/PlayMode gate at the end.

Mandatory PlayMode behaviors include:

- Bootstrap composition and EventSystem;
- Ashfall hydration;
- fake activity -> accepted steps -> Vitality -> restoration;
- offline production with deterministic clock;
- placement move/rotation -> save -> reload;
- Builder/Explore canonical transform parity;
- permission denial remaining non-blocking;
- stale Expedition marker boot recovery;
- corrupt-save fail-closed boot preserving bytes;
- explicit start-over quarantining evidence and recomposing runtime.

Add focused PlayMode regressions for any Unity-only defect found.

### W5 — Android release-shaped development build

Use existing build entry point and preserve its core constraints:

- package `com.quantdale.walkgame`;
- minSdk 26;
- targetSdk 35 unless current toolchain requires a documented update;
- ARM64;
- IL2CPP;
- development + debugging allowed for certification build;
- enabled Bootstrap scene(s) only as configured.

Before building, verify Android Build Support, SDK, NDK and JDK are actually available to the pinned editor. If module installation requires administrator elevation/user interaction, record the blocker; do not bypass it.

A successful build must produce:

- APK path;
- Unity build log;
- file size;
- SHA-256;
- source git SHA;
- backend/architecture/SDK-level summary.

### W6 — Android emulator/device lifecycle smoke

The generic smoke gate may run on an emulator for app lifecycle only, but the result must be labeled accordingly.

Required lifecycle actions:

- clean install/data clear;
- cold launch;
- Bootstrap stability;
- background/resume;
- orientation/aspect change where supported;
- force-stop/relaunch;
- fatal exception/ANR sweep.

Capture screenshots for startup and representative Builder/Explore states where feasible, but screenshots never replace log/process evidence.

### W7 — Physical Android movement correctness

Run only on a physical Android device that reports `android.hardware.sensor.stepcounter`.

Execute the repository physical checklist, prioritizing release-blocking movement cases:

- first motion permission request;
- denial and later grant;
- first baseline;
- known walking sample with screen off;
- background walk;
- force-stop/process-death recovery;
- phone reboot/counter reset;
- Walk Expedition completion;
- location denied fallback;
- duplicate-credit probe.

For every movement case record before/after:

- lifetime accepted steps;
- Vitality balance;
- relevant recent transaction IDs/reason codes if available through sanctioned debug tooling;
- provider/reconciliation log evidence that does not expose prohibited raw sensitive data.

Do not use raw GPS route capture as certification evidence unless an existing optional Expedition flow legitimately produces it and privacy docs permit it. Passive movement must remain GPS-free.

### W8 — Player-visible mobile runtime audit

On physical Android, manually verify the vertical slice rather than only the sensor:

- safe-area layout and readability;
- touch targets/buttons;
- builder pan/zoom/selection;
- move preview/confirm/cancel;
- joystick/explore movement and return-to-builder;
- project panel and producer collection;
- onboarding progression;
- permission banner copy;
- Expedition start/finish/failure copy;
- audio settings and persistence rollback truthfulness where fault injection is available;
- save-recovery screen if a controlled test slot can be used without destroying real user data.

Record failures with screenshot/video and exact reproduction steps. Fix blocker/high usability defects that invalidate M8 certification; defer purely aesthetic polish.

### W9 — Performance / battery / thermal baseline

Select at least one available physical Android device and record its class/specs.

Measure separate scenarios:

1. Builder View steady state;
2. Explore View steady state;
3. Builder <-> Explore transition/hitch;
4. 20–30 minute active session / Expedition where practical.

Capture available metrics:

- average and worst/relevant frame time or FPS;
- main-thread/renderer markers if profiler connection is practical;
- GC allocations / collections;
- memory footprint;
- battery delta / batterystats;
- thermal status;
- app crashes/ANRs;
- region transition timing if applicable.

Target philosophy remains repository-defined: 30 FPS minimum on selected low/mid hardware, 60 FPS on stronger devices where practical. A miss is not permission to hide the measurement; it becomes a measured follow-up or in-campaign fix depending on severity.

### W10 — iOS lane

If macOS + Xcode + signing + an iOS device are genuinely available:

- generate Xcode project;
- verify `NSMotionUsageDescription` post-build injection;
- build/sign/install;
- run iOS checklist I1–I7 with artifacts.

If unavailable, do not simulate DEVICE evidence. Inspect deterministic source/config readiness only, record blocker and leave iOS tier UNVERIFIED.

## 3. Defect handling policy

During certification:

- **Critical/High:** fix in campaign, add regression, rerun all affected downstream gates.
- **Medium correctness/certification integrity:** fix if necessary for truthful M8 evidence.
- **Medium/Low polish:** document unless cheap and clearly within scope.
- **Performance:** change only after measured evidence; include before/after on same device/scenario.

Any collision with persistence/activity exactly-once invariants triggers the full relevant headless regression family before device re-certification.

## 4. Documentation / ADR policy

A new ADR is required only if certification forces a material architecture or evidence-policy decision. Small script hardening and namespace fixes do not need an ADR by themselves.

Update:

- `docs/IMPLEMENTATION_STATUS.md` with fresh evidence matrix;
- `docs/TESTING_AND_PERFORMANCE.md` with measured baselines and new gate semantics;
- `docs/DEVICE_CERTIFICATION_CHECKLISTS.md` if evidence procedure changes;
- architecture/mobile/activity/privacy docs only when implementation behavior actually changes;
- this OpenSpec's task status and final evidence footer.

## 5. Completion semantics

M8.6 may finish with some external tiers still UNVERIFIED, but only if:

- every locally executable gate is exhausted;
- each unavailable tier has a specific external prerequisite/blocker;
- no discovered Critical/High defect is left open merely because another tier is blocked;
- no PASS claim lacks its required artifact;
- final report identifies what is required for the next operator/device run.

If Android editor/build/device certification is substantially green, the next campaign should be **M9 closed-playtest readiness/validation**, not Region 2. If real device results expose material performance or movement defects, the next campaign should target the measured blocker instead.
