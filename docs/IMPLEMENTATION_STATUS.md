# Implementation Status

Tracks what exists in code versus the ROADMAP phases, tiered by the evidence actually
produced. Acceptance criteria that require physical devices or an installed editor are
deliberately left unclaimed.

Evidence tiers:

- **AUTOMATED** — verified by the standalone domain suite (`scripts/verify-domain.ps1`).
- **EDITOR** — requires a Unity 6000.3.x editor; none available in this environment.
- **DEVICE** — requires physical/emulated mobile hardware.
- **UNVERIFIED** — claimed by no evidence yet.

Last updated: 2026-08-23 (Unity bring-up & activity hardening campaign)

## Verification status

- Domain test suite: **97/97 passing (AUTOMATED)** via
  `dotnet test verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj`
  (`scripts/verify-domain.ps1`; also runs in CI on every push/PR to `main`).
- CI domain gate: configured (`.github/workflows/domain-tests.yml`) — **AUTOMATED**.
- Unity editor compile, EditMode, Play Mode, Android build/install/launch, iOS Xcode
  generation/build: **UNVERIFIED — no Unity installation, macOS/Xcode, or physical
  devices exist in the certification environment.** The batch-mode entry point
  (`scripts/verify-unity-editmode.ps1`) is committed and waiting on an editor.
- Static bring-up audit of assemblies/GUIDs/scenes/packages: performed by hand; see
  Phase 0 fixes below.

## Phase 0 - Foundation

| Item | State |
| --- | --- |
| Pin Unity 6000.3.4f1 | ProjectVersion.txt pins it; first Hub open resolves toolchain — **UNVERIFIED** (no editor) |
| URP project | Package pinned + setup menu assigns pipeline asset on first open — **UNVERIFIED** (editor step) |
| Android/iOS build profiles | Editor menu (`WalkGame/Setup/Apply Product Identity`) — **UNVERIFIED** |
| .gitignore | Done; verification harness projects are commit-included |
| Assembly definitions | All twelve assemblies present. Campaign fix (ADR 0004): platform assemblies compile on every target with source-level `#if` guards; `noEngineReferences` corrected for the Android JNI interop assembly. Missing deterministic `.meta` for the editor tools script added — **AUTOMATED** (static audit) |
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
Gray-box terrain visuals pending first-editor-open art pass (**UNVERIFIED**).

## Phase 2 - Restoration and builder loop

VitalityLedger, debug provider (+1,000 steps), step-to-Vitality conversion, project
definitions/prerequisites/transactions, ruin-to-restored swap, builder camera,
placement grid validation, move preview/confirm/cancel, persisted placement incl.
save/reload identity — **AUTOMATED**. On-device touch behavior — **UNVERIFIED**.

## Phase 3 - Explore mode

Third-person controller + follow camera, explicit mode state machine, authored spawn,
canonical state shared by both views, boundary clamping, NPC/lore as domain state —
**AUTOMATED**. Scene-side NPC actors/ambience remain stubs for the art pass.

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
  **UNVERIFIED — no hardware/emulator-with-sensor available for this repo's gates.**

## Phase 5 - Idle/incremental systems

ProductionService checkpoint math, offline cap, backward-clock clamp, storage caps,
tier multipliers, collection API + dedicated HUD collect buttons refreshed on ticker
cadence — **AUTOMATED**.

## Phase 6 - Vertical-slice content

Ashfall Basin: 15 restoration projects, 9 building instances, 4 producers, 3 NPCs,
5 lore objects, milestone ladder, stages 0-3, scripted full-playthrough test reaching
the transit-gate finale — **AUTOMATED**. Visual stages are gray-box tints pending art/audio.

## Exactly-once movement rewards

Central invariant enforced across passive polling, Expeditions, process restarts and
save reloads (campaign S8): session-id dedup store, passive suppression during active
sessions with cursor partitioning, durable interval keys (a serializer defect that had
been silently dropping them was found and fixed). Regression suites:
`ActivityServiceTests`, `AndroidCounterReconciliationTests`, `SaveLoadTests` —
**AUTOMATED**. Real-device double-count probing — **UNVERIFIED**.

## Phases 7-9

Not started by design: performance/privacy hardening requires physical devices;
playtest and post-MVP expansion follow the roadmap gates.

## Environment record for this campaign

- Start SHA `2727ec0`, clean tree, synced with origin/main at session start.
- Available tooling: .NET SDK 8.0.424; JDK 17; Android SDK (platforms 35/36,
  build-tools 35/36, NDK 27.x) with a running API-36 emulator (`emulator-5554`).
- Not available: any Unity editor, macOS/Xcode, physical iOS/Android hardware.
- Honest consequence: all EDITOR- and DEVICE-tier gates above remain UNVERIFIED even
  where the underlying code was fixed and domain-tested.
