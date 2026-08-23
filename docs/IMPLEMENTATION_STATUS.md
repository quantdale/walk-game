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

Last updated: 2026-08-23 (M8 device-ready certification & release hardening campaign)

## Verification status

- Domain test suite: **124/124 passing (AUTOMATED)** via
  `dotnet test verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj`
  (`scripts/verify-domain.ps1`; also runs in CI on every push/PR to `main`).
- CI domain gate: configured (`.github/workflows/domain-tests.yml`) now including the
  release-hygiene/privacy audit — **AUTOMATED**.
- Unity `6000.3.4f1` is installed at `C:\UnityEditors\6000.3.4f1\Editor`, its executable
  reports the pinned version, and `Data\Resources\PackageManager\Server\UnityPackageManager.exe`
  is present. M8 re-investigation from scratch confirmed the licensing gate is account-level:
  Unity Hub 3.21.0 (MSIX) runs but holds **zero logged-in accounts** (`accounts.db` empty), the
  licensing client reports "Token not found in cache" with 0 entitlement groups and no ULF,
  and every editor entitlement resolves to `granted: False`. No offline activation path exists
  without user credentials; fabricating or bypassing licensing is prohibited. Unity compile,
  EditMode, and PlayMode therefore remain **UNVERIFIED** (reproducible gate: sign into Hub,
  activate a license, run `scripts/setup-unity-project.ps1`, `scripts/verify-unity-editmode.ps1`,
  `scripts/verify-unity-playmode.ps1`).
- Android Build Support remains absent (`AndroidPlayer` missing; only Windows Standalone is
  installed). A prior Hub module-install attempt is recorded as paused with status
  `install-queued`, `ELEVATION_CANCELLED`; this session cannot elevate. The committed build
  entry point now certifies the release-shaped backend (IL2CPP + ARM64, minSdk 26, targetSdk 35).
  Android build/install/launch and native lifecycle evidence remain **UNVERIFIED**;
  `scripts/verify-android-smoke.ps1` is committed and ready for the first emulator/device.
  iOS Xcode generation/build remains **UNVERIFIED** (no macOS/Xcode).
- Static bring-up audit of assemblies/GUIDs/scenes/packages: **AUTOMATED**; 94 asset files
  and 94 `.meta` files pass the audit with zero missing real GUID references. The only unresolved
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

## M8 runtime-defect record

Defects discovered by first-import/runtime inspection and fixed this campaign:

1. **Stale Expedition suppression marker** — a process kill mid-Expedition persisted
   `activeSession`, which suppressed every future passive snapshot forever (no recovery path
   existed). Fixed with boot-time `ActivityService.RecoverInterruptedSession()`; recovery
   credits nothing itself and movement made during the interruption re-reads from the provider
   cursor exactly once. Covered by `InterruptedSessionRecoveryTests` + PlayMode boot gate.
2. **Missing uGUI EventSystem** — all UI was composed programmatically but nothing ever created
   an EventSystem, so every button and the Explore joystick rendered yet were inert. Fixed via
   `UiRuntime.EnsureEventSystem` during UI composition; PlayMode regression gate added.
3. **Literal `\n` in project panel text** — restoration rows/details displayed raw backslash-n
   instead of line breaks. Fixed strings in `ProjectPanelController`.
4. **Event-subscription leaks** — `UiComposer`/`AppFlowController` subscribed to domain events
   without ever unsubscribing; stale handlers would fire into destroyed UI across scene reloads.
   Both now detach every subscription on destroy.
5. **Silent feedback layer** — audio cues were wired but no clips existed anywhere, so settings
   showed values with no audible effect. Procedural stand-in cues + honest ambient loop now
   honor master/music/effects/haptics until final production audio lands.
6. **Dead-end finale copy** — completing all 15 projects still pointed players at "the next
   locked project". Full completion now communicates the milestone cleanly (§19).

## Environment record for this campaign

- Start SHA `bc3fd7f9831ebacda936090266abe5d73ebd0d45` (M8 campaign), clean tree, synced
  with `origin/main`. Checkpoints `3a0244a` (runtime bring-up fixes) and `98776ac`
  (Android/smoke/hygiene gates) are committed to `main`.
- Available tooling: .NET SDK 8.0.424; Unity Hub 3.21.0 (MSIX, running); exact Unity editor
  `6000.3.4f1`; JDK 17.0.20; Android SDK platforms 36/37.0, build-tools 35/36,
  NDK 27.0/27.1; `adb` 37.0.1; API-36 emulator `emulator-5554` (no TYPE_STEP_COUNTER).
- Licensing/installation (M8 re-investigation): the licensing client is healthy and online
  but no Unity account session exists on the machine (`accounts.db`: zero accounts; client
  log: "Token not found in cache"; all entitlements `granted: False`). The prior module
  install died at UAC elevation (`ELEVATION_CANCELLED`) and this session is not elevated.
  Activation requires a user account sign-in outside this environment.
- Not available: macOS/Xcode, physical iOS/Android hardware, an Android emulator exposing a
  genuine step-counter sensor, and a licensed Unity editor session.
- Honest consequence: all EDITOR- and DEVICE-tier gates above remain **UNVERIFIED** even
  where the underlying code was fixed and domain-tested. Every blocked gate is reproducible
  via the scripts named in this document once its specific precondition (license, module,
  or hardware) is met.

## M8 certification matrix

| Gate | Result | Evidence |
| --- | --- | --- |
| Domain suite | PASS | 124/124 (`dotnet test`, this campaign) |
| Unity static audit | PASS | 94 assets/94 metas, pin + manifest invariants (`verify-unity-static.ps1`) |
| Release hygiene / privacy audit | PASS | 57 runtime sources, minimal manifest (`verify-release-hygiene.ps1`) |
| `git diff --check` | PASS | clean output at final gate run |
| Unity project import | UNVERIFIED | requires licensed editor |
| Unity compilation | UNVERIFIED | requires licensed editor |
| EditMode | UNVERIFIED | committed sources compile under the standalone harness; editor run blocked by license |
| PlayMode | UNVERIFIED | suite extended (EventSystem + boot-recovery gates); editor run blocked by license |
| Ashfall complete playthrough | PASS | `AshfallTests.DeadWorld_To_TransitGateAlignment_CompletesInDependencyOrder` |
| Economy pacing replay | PASS | `AshfallEconomyPacingTests` (casual-walker window; idle-only completes nothing) |
| Exactly-once activity | PASS | `ActivityServiceTests` + `InterruptedSessionRecoveryTests` (incl. save/reload) |
| Save fault injection | PASS | `SaveLoadTests` (+ producer-prune regression) |
| Android build | UNVERIFIED | AndroidPlayer module absent; build script release-shaped and ready |
| Android install/launch/lifecycle smoke | UNVERIFIED | `verify-android-smoke.ps1` committed for first emulator/device |
| Real step sensor | UNVERIFIED | physical device required |
| iOS compile/device | UNVERIFIED | no macOS/Xcode |
| Performance measurement | UNVERIFIED | structural measures only; profiling needs hardware |
