# M8.6 Audit — Unity First-Import & Device Readiness Certification

**Status:** PLANNED / NOT IMPLEMENTED  
**Repository:** `quantdale/walk-game`  
**Planned-From:** `main@3bbdbcca11fb20a6680dbb96e808b9df2cca31f3`  
**Audit date:** 2026-08-26  
**Campaign class:** first real Unity compile / PlayMode / Android build & device certification / evidence hardening  
**Target milestone:** M8 — Device Ready

## 1. Executive verdict

M8.5 closed the last known high-leverage, headlessly reproducible state-integrity tranche. Current repository evidence is strong at the engine-free layer: the standalone suite reports 213/213 passing, the static Unity checkout audit reports 107 source assets / 107 meta files, and release-hygiene/privacy checks pass. The repository's own M8.5 handoff explicitly recommends real Unity/device certification next rather than manufacturing another speculative headless-hardening cycle.

The next campaign is therefore **M8.6 Unity First-Import & Device Readiness Certification**.

This is not a cosmetic QA pass. The repository has never produced fresh licensed Unity compile/EditMode/PlayMode evidence for the current tree, has not produced a current Android IL2CPP/ARM64 build with the required Build Support installed, and has not certified the real Android step-counter lifecycle on physical hardware. Those are now the dominant risks.

The audit also found concrete certification-layer defects/risks that should be reproduced and repaired as part of this campaign rather than deferred:

1. **Likely editor-only compile blockers in `WalkGameEditorTools.cs`.** The file uses `GraphicsSettings` (namespace `UnityEngine.Rendering`) and implements `IPostprocessBuildWithReport` (namespace `UnityEditor.Build`) without importing those namespaces. The standalone `.NET` harness intentionally does not compile `Assets/WalkGame/Editor/**`, and the static Unity audit intentionally does not perform a semantic C# compile. These references therefore remain unproven until a real editor import/compile.
2. **EditMode runner can overstate success.** `scripts/verify-unity-editmode.ps1` exits with Unity's exit code but, unlike the PlayMode runner, does not fail when Unity returns zero without producing `TestResults/editmode-results.xml`. Certification evidence must require the artifact, not only the process exit code.
3. **Android smoke target selection is not fail-closed.** `scripts/verify-android-smoke.ps1` accepts a list containing multiple devices/emulators and then invokes `adb` without `-s`; the physical-device checklist requires exactly one target. The script should either fail early on ambiguity or accept an explicit serial and bind every command to it.
4. **Current M8 evidence is incomplete by construction.** Unity compile, EditMode, PlayMode, JNI/CoreMotion callback behavior, Android Build Support, real step sensor, physical battery/thermal, touch/safe-area/visual behavior, and iOS/Xcode remain UNVERIFIED in the authoritative status document.

These are the correct next risks to retire before Region 2, HealthKit/Health Connect, cloud/social features, art breadth, or speculative performance optimization.

## 2. Audit coverage

The planner enumerated the complete recursive repository tree at current `main` and reconciled it against the completed M8.5 OpenSpec, recent implementation commits, `AGENTS.md`, the authoritative implementation status, roadmap, architecture/data/activity/mobile/privacy/testing docs, certification checklists, CI, build scripts, project settings, runtime tests, native plugins, and representative post-M8.5 runtime ownership code.

Coverage method:

- **Logic-bearing C#/Kotlin/Objective-C/PowerShell/shell/YAML/JSON/asmdef/config files:** semantic review, with special attention to compile boundaries, platform guards, lifecycle, persistence, activity exactly-once, build/certification tooling, and evidence truthfulness.
- **Unity scenes/project settings/manifest/package files:** structural and contract review against the static verifier and build/runtime entry points.
- **`.meta` files:** structural integrity is delegated to the repository's deterministic GUID/meta audit; metadata is not treated as executable logic.
- **Tests:** reviewed as evidence boundaries: engine-free tests prove the domain/activity/persistence protocols, while PlayMode/native/device suites are intentionally the missing tiers.
- **Docs/OpenSpec/ADRs:** reconciled against current implementation claims and the M8.5 completion record.

Representative audited surfaces include:

- repository identity, writer-lock, remote-advance, hooks, CI, and agent handoff files;
- `Core`, `Activity`, `Persistence`, `Building`, `Gameplay`, `Content` domain assemblies;
- `App`, `UI`, `World`, `Editor` Unity-only assemblies;
- Android Kotlin sensor bridge and Android provider;
- iOS CoreMotion bridge/provider;
- EditMode/headless/PlayMode tests;
- Android build/smoke scripts and physical-device checklist;
- package manifest, project version, build settings, scenes, asmdefs and metadata;
- all current docs/ADRs and completed M8.5 OpenSpec.

At planning time there are no open pull requests or open issues competing with this campaign.

## 3. Current baseline and evidence boundary

Current main is `3bbdbcca11fb20a6680dbb96e808b9df2cca31f3` (`feat(activity): M8.5 runtime ownership & rollback fidelity (ADR 0011)`). Historical M8.5 evidence reports:

- standalone domain suite: **213/213 PASS**;
- Unity static audit: **107/107 source/meta parity PASS**;
- release hygiene/privacy audit: **PASS**;
- PowerShell agent-guard tier: **PASS**;
- Unity compile/EditMode/PlayMode: **UNVERIFIED**;
- Android development/release-shaped build: **UNVERIFIED** because Android Build Support was absent;
- Android real step-counter lifecycle: **UNVERIFIED** because the available API-36 emulator exposes no genuine `TYPE_STEP_COUNTER`;
- iOS Xcode/build/device: **UNVERIFIED** because macOS/Xcode/signing are unavailable;
- physical FPS/GC/memory/battery/thermal: **UNVERIFIED**.

The M8.6 executor must rerun every locally available baseline gate. Historical counts are context only, never fresh evidence.

## 4. Confirmed / high-confidence planner findings

### H1 — Unity Editor assembly has uncompiled namespace references

`Assets/WalkGame/Editor/WalkGameEditorTools.cs` references:

- `GraphicsSettings.defaultRenderPipeline` without `using UnityEngine.Rendering;` or a fully qualified name;
- `IPostprocessBuildWithReport` without `using UnityEditor.Build;` or a fully qualified name.

The standalone test project compiles only engine-free `Core`, `Building`, `Gameplay`, `Activity`, `Persistence`, `Content`, and EditMode test sources. It deliberately excludes Editor/App/UI/World/platform Unity assemblies. `verify-unity-static.ps1` validates files, GUIDs, packages, manifest invariants and build-scene presence, but explicitly does not import Unity or claim compile evidence.

**Required disposition:** first obtain a real Unity import/compile result. If these references fail exactly as predicted, fix the smallest namespace/assembly issue and add a deterministic guard where practical so this class of editor-only compile break cannot silently recur. Do not claim the prediction as a reproduced compiler defect until Unity emits the error.

### H2 — EditMode certification does not require its result artifact

`scripts/verify-unity-editmode.ps1` prints success when Unity exits 0, even if `editmode-results.xml` was not created. The PlayMode runner already fails on the corresponding missing artifact.

**Required disposition:** make EditMode evidence fail-closed: exit 0 is necessary but not sufficient; the test-results XML must exist and represent a completed run. Prefer parsing or minimally validating the result document if repository dependencies permit it without adding a fragile framework.

### H3 — Android smoke is ambiguous with multiple adb targets

The script enumerates every connected device/emulator but does not enforce one target and does not add `adb -s <serial>` to subsequent commands. The physical-device checklist explicitly requires exactly one device under test. With multiple targets, commands can fail later with `more than one device/emulator`, producing noisy and non-deterministic evidence.

**Required disposition:** add explicit target identity. Either fail closed unless exactly one device is connected, or accept a serial argument and bind every adb operation to it. The summary artifact must record the exact serial, model, API level, ABI, and whether the target exposes a genuine step counter.

### H4 — First-import/runtime certification is the largest remaining blind spot

`RuntimeCertificationTests` is substantial but has never been executed on the current tree in a licensed Unity environment. It covers Bootstrap composition, EventSystem, state hydration, activity->Vitality->restoration, production, placement persistence, Builder/Explore canonical projection, permission denial, stale Expedition recovery, and fail-closed save recovery.

Until the suite runs, Unity-only code paths—including `GameHost`, `AppFlowController`, `ActivityTicker`, `ExpeditionController`, `UiComposer`, HUD, cameras, environment presenter, platform adapters, and editor tooling—remain vulnerable to compile/import/runtime defects that the standalone test project cannot see.

**Required disposition:** treat the first editor import and compile as P0. No Android/device certification should be called meaningful until all compile errors are resolved and EditMode/PlayMode are green or each failure has a documented disposition.

### H5 — Device evidence must be artifact-backed, not narrative

The current physical-device checklist already defines a strong evidence rule: a case is PASS only with its screenshot/logcat/trace and recorded date, device, OS, build ID and artifact path. M8.6 must preserve that discipline and improve automation around evidence collection where justified.

**Required disposition:** every executed device case must yield stable artifacts under an ignored artifact directory and a machine-readable summary. `docs/IMPLEMENTATION_STATUS.md` must contain only claims supported by those artifacts.

## 5. Areas re-audited without a new headless campaign blocker

No new Critical/High reason was found to reopen the already-hardened engine-free correctness work in:

- Vitality ledger and reward calculation;
- restoration prerequisites/transactions;
- building placement/grid/rotation rules;
- production/offline cap arithmetic;
- Builder/Explore canonical transform state;
- save atomicity, quarantine, forward-schema refusal, and rollback graph fidelity;
- activity prepared-delivery/transaction coordination;
- provider lifetime/operation ownership;
- Android claim identity and dedup canonicalization;
- passive-without-GPS privacy invariant;
- Ashfall Basin content breadth.

If a real Unity/device run uncovers a Critical/High defect in these systems, fix it and extend the lowest practical regression tier. Do not proactively redesign systems that are already green headlessly.

## 6. Why M8.6 before M9 or new content

The roadmap defines M8 Device Ready as the point where performance/privacy/lifecycle behavior is hardened on target devices. M9 is closed-playtest validation, and Region 2 is explicitly post-MVP expansion. Shipping more content before the current vertical slice compiles, builds and survives real mobile lifecycle/sensor/performance testing would increase uncertainty rather than reduce it.

M8.6 therefore prioritizes risk in repository order:

1. state integrity under real runtime lifecycle;
2. real movement reward correctness;
3. Builder/Explore projection in PlayMode/device runtime;
4. player-visible restoration/permission/save-recovery truthfulness;
5. measured mobile performance/battery/thermal;
6. only then broader playtest/content work.

## 7. 12-hour execution posture

The executor should treat this as an **up-to-12-hour autonomous certification campaign**. It must not stop after the first compile fix or first green test. Continue down the prioritized workstreams while useful work remains.

However, elapsed time is not a completion criterion. Do not fabricate activity merely to consume 12 hours. If all locally executable requirements are complete earlier, finish honestly. If licensing, administrator elevation, Android modules, physical hardware, macOS/Xcode, or other external prerequisites block a lane, capture reproducible diagnostics and move to another legitimate lane. Never bypass licensing, security, signing, or device requirements.

## 8. Planning boundary

This planner branch contains no gameplay implementation. The executor must:

- reacquire current repository truth after fetch/pull;
- run repository identity and writer-lock rules before mutation;
- reconcile any main advancement since `3bbdbcca`;
- read the complete M8.6 OpenSpec package;
- reproduce predicted defects before claiming them fixed where the environment permits;
- execute all available Unity/build/device gates;
- fix any Critical/High blocker and required Medium certification-integrity defect found;
- preserve the M8.5 correctness invariants;
- leave unavailable external tiers explicitly UNVERIFIED with exact blockers;
- update docs/OpenSpec/evidence and commit/push a detailed final session report.
