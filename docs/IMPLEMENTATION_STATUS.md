# Implementation Status

Tracks what exists in code versus the ROADMAP phases, tiered by the evidence actually
produced. Acceptance criteria that require physical devices or an installed editor are
deliberately left unclaimed.

Evidence tiers:

- **AUTOMATED** — verified by the standalone domain suite (`scripts/verify-domain.ps1`).
- **EDITOR** — requires the pinned Unity editor with a valid license and is only claimed
  when the editor compile/test logs prove it.
- **DEVICE** — requires physical/emulated mobile hardware.
- **UNVERIFIED** — claimed by no evidence yet.

Last updated: 2026-08-23 (Ashfall Basin player-facing development campaign)

## Verification status

- Domain test suite: **113/113 passing (AUTOMATED)** via
  `dotnet test verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj`
  (`scripts/verify-domain.ps1`; also runs in CI on every push/PR to `main`).
- CI domain gate: configured (`.github/workflows/domain-tests.yml`) — **AUTOMATED**.
- Unity `6000.3.4f1` is installed at `C:\UnityEditors\6000.3.4f1\Editor`, its executable
  reports the pinned version, and `Data\Resources\PackageManager\Server\UnityPackageManager.exe`
  is present. Batch setup and the committed EditMode wrapper still terminate at the
  licensing gate (wrapper exit 127; Unity licensing log reports no ULF, no token, and
  zero entitlement groups), before project compilation. Unity compile, EditMode, and
  PlayMode therefore remain **UNVERIFIED**.
- Android Build Support is not present in the editor (`AndroidPlayer` is absent), and no
  licensed editor build could run.
  Android build/install/launch and native lifecycle evidence remain **UNVERIFIED**.
  iOS Xcode generation/build remains **UNVERIFIED** (no macOS/Xcode).
- Static bring-up audit of assemblies/GUIDs/scenes/packages: **AUTOMATED**; 91 asset files
  and 91 `.meta` files pass the audit, with zero missing real GUID references. The only unresolved
  scene GUID is Unity's built-in zero GUID for the authored light.

## Phase 0 - Foundation

| Item | State |
| --- | --- |
| Pin Unity 6000.3.4f1 | ProjectVersion.txt pins it; exact editor executable reported `6000.3.4f1` — **AUTOMATED/environment**; licensed import **UNVERIFIED** |
| URP project | Package pinned + setup menu assigns pipeline asset on first open — **UNVERIFIED** (editor step) |
| Android/iOS build profiles | Editor menu (`WalkGame/Setup/Apply Product Identity`) and batch entry point — **UNVERIFIED** (editor installation/license gate) |
| .gitignore | Done; verification harness projects are commit-included |
| Assembly definitions | All assemblies present. Campaign fix (ADR 0004): platform assemblies compile on every target with source-level `#if` guards; `noEngineReferences` corrected for the Android JNI interop assembly. Missing deterministic `.meta` files added — **AUTOMATED** (static audit; Unity compile unverified) |
| Input System | Package pinned; active-input toggle in setup menu — **UNVERIFIED** (editor step) |
| Test assemblies | WalkGame.Tests.EditMode + standalone harness share sources — **AUTOMATED** |
| Bootstrap scene | Scene YAML valid; GameHost GUID reference matches its committed meta — **AUTOMATED** (static); Play Mode **UNVERIFIED** |
| Service composition root | `App/GameHost.cs`; provider selection never falls back to debug for permission reasons — **AUTOMATED** |
| ClockService | IClock + system/mutable/offset variants; all economic/lifecycle time flows through it (ADR 0005-era campaign fix) — **AUTOMATED** |
| Save serializer/repository | Atomic writes + backup recovery + sequential migration — **AUTOMATED** |
| Logging wrapper | Done — **AUTOMATED** |
| Debug/dev menu shell | Done, dev-build gated — **AUTOMATED** (domain-level) |

## Phase 1 - Gray-box region and canonical state

RegionDefinition/RegionState/stable IDs per DATA_MODEL.md; Ashfall Basin
content-as-code with integrity tests; buildings instantiate from state via
`RegionPresenter`/`BuildingActor`; single-scene MVP load; save schema v1 with
sequential migrator; deterministic fixtures — **AUTOMATED**.
The runtime now contains a reusable procedural Ashfall environment kit (routes, settlement,
river/waterworks, grove/wetland, workshop, greenhouse, research, residential district,
transit gate, story spaces, and stage-driven ambience). Code/static readiness is
**AUTOMATED**; editor visual inspection remains **UNVERIFIED**.

## Phase 2 - Restoration and builder loop

VitalityLedger, debug provider (+1,000 steps), step-to-Vitality conversion, project
definitions/prerequisites/transactions, ruin-to-restored swap, builder camera,
placement grid validation, move preview/confirm/cancel, persisted placement incl.
save/reload identity — **AUTOMATED**. Responsive touch/UI paths are implemented; on-device
touch behavior — **UNVERIFIED**.

## Phase 3 - Explore mode

Third-person controller + follow camera, explicit mode state machine, authored spawn,
canonical state shared by both views, boundary clamping, NPC/lore as domain state —
**AUTOMATED**. Scene-side NPC actors, lore markers, interaction prompts, authored anchors,
and stage ambience are implemented; PlayMode visual behavior — **UNVERIFIED**.

## Phase 4 - Native activity integration

Campaign-hardened (see ADR 0005):

- Permission lifecycle: contextual request API on every provider, coordinator state
  machine, denial-as-normal-state, Android lazy monitoring + Denied/NotDetermined
  refinement, iOS real first-request path — **AUTOMATED** (fake providers).
- Android cumulative counter: engine-free reconciler covering unknown-start baseline,
  reboot reset, NaN/infinity/negative fail-closed, repeated values, implausible-jump
  cap, persisted cursor across process restarts — **AUTOMATED**
  (`AndroidCounterReconciliationTests`).
- Non-blocking pipeline: no `.Result`/`.Wait()` on gameplay paths; async iOS bridge
  with stale-result protection; failed queries leave cursors untouched — **AUTOMATED**
  at planning/domain level; native callback marshalling itself is **UNVERIFIED**
  (requires device/Xcode).
- Device checklist (permission dialogs, reboot/resume on hardware, logcat evidence):
  **UNVERIFIED**. An API-36 emulator (`sdk_gphone64_x86_64`, `emulator-5554`) is
  connected, but `dumpsys sensorservice` exposes no `TYPE_STEP_COUNTER`; it cannot
  certify real movement. The committed Android bridge now reports unavailable when the
  sensor is absent, and the fake-provider lifecycle cases remain **AUTOMATED**.

## Phase 5 - Idle/incremental systems

ProductionService checkpoint math, offline cap, backward-clock clamp, storage caps,
tier multipliers, collection API, offline resume summary, producer status rows, and
dedicated HUD collect buttons refreshed on ticker cadence — **AUTOMATED**.

## Phase 6 - Vertical-slice content

Ashfall Basin: 15 restoration projects, 9 building instances, 4 producers, 3 NPCs,
5 lore objects, milestone ladder, stages 0-3, scripted full-playthrough test reaching
the transit-gate finale — **AUTOMATED**. Canonical-state-derived visual stages, restoration
feedback hooks, responsive project/producer HUD, onboarding, and Expedition presentation
are code/static-ready; Unity runtime presentation — **UNVERIFIED**.

## Exactly-once movement rewards

Central invariant enforced across passive polling, Expeditions, process restarts and
save reloads (campaign S8): session-id dedup store, passive suppression during active
sessions with cursor partitioning, durable interval keys (a serializer defect that had
been silently dropping them was found and fixed). Regression suites:
`ActivityServiceTests`, `AndroidCounterReconciliationTests`, `SaveLoadTests` —
**AUTOMATED**. Real-device double-count probing — **UNVERIFIED**.

## Phase 7A — non-hardware hardening

- Persistence fault injection: **AUTOMATED**. Tests cover interruption before temp
  completion, failure after temp creation, backup-copy failure, main deletion before
  replacement, missing/corrupt main with valid backup, both files corrupt, unsupported
  schema, migration failure, and injected write/copy failures. The repository reports
  failure and preserves the last recoverable save; it never silently wipes it.
- Clock/time anomalies: **AUTOMATED**. Tests cover backward time, future timestamps,
  offline caps, UTC persistence, and fake-clock production. Future restoration timestamps
  produce an explicit validation anomaly report and warning, while production rejects
  future elapsed time rather than granting progression.
- Privacy/logging: **AUTOMATED/static audit**. No raw GPS coordinates, continuous routes,
  or native sensor payloads are logged or persisted. The Android manifest declares only
  optional step-counter hardware plus `ACTIVITY_RECOGNITION`; no location permission is
  mandatory. Save diagnostics no longer include local save paths. Release logging is
  warning-level; device log review remains **UNVERIFIED**.
- Performance readiness: **AUTOMATED/static review**. The presentation uses shared
  materials/property blocks for repeated geometry, reused UI rows, a non-allocating
  Explore interaction scan, checkpoint production math, and reduced-motion gating for
  particles. No FPS/allocation/thermal numbers are claimed; physical-device profiling
  remains **UNVERIFIED**.

## Phases 7B-9

Physical Android/iOS lifecycle, sensor, performance, store, playtest, and post-MVP
expansion gates remain unverified or intentionally out of scope for this campaign.

## Environment record for this campaign

- Start SHA `c8fe24d792ea8f2eab7d80b62d96354a62f81727`, clean tree, synced with
  `origin/main` at campaign start. Player-facing checkpoint `11735af` is committed and
  pushed to `main`.
- Available tooling: .NET SDK 8.0.424; Unity Hub 3.21.0; exact Unity editor
  `6000.3.4f1`; JDK 17.0.20; Android SDK platforms 36/37.0, build-tools 35/36,
  NDK 27.0/27.1; `adb` 37.0.1; API-36 emulator `emulator-5554`.
- Licensing/installation: no Unity access token or entitlement was available. A manual
  activation file was generated for handoff and removed from the repository; activation
  requires a user account/manual step outside this environment. Package Manager server
  integrity is now confirmed, but the editor still lacks a valid license and Android player
  module, so Unity compilation or APK generation cannot be certified here.
- Not available: macOS/Xcode, physical iOS/Android hardware, and an Android emulator
  exposing a genuine step-counter sensor.
- Honest consequence: all EDITOR- and DEVICE-tier gates above remain **UNVERIFIED** even
  where the underlying code was fixed and domain-tested.
