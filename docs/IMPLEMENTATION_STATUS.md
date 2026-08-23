# Implementation Status

Tracks what exists in code versus the ROADMAP phases. Acceptance criteria that require
physical devices or an installed editor are deliberately left unclaimed.

Last updated: 2026-08-23

## Verification status

- Domain test suite: **64/64 passing** via
  `dotnet test verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj`.
  The same sources compile as Unity EditMode tests (`WalkGame.Tests.EditMode`).
- Unity editor compile/play and device builds: **not yet executed** (no editor/license
  in bootstrap environment; see docs/adr/0003-hand-bootstrapped-project.md).

## Phase 0 - Foundation

| Item | State |
| --- | --- |
| Pin Unity 6.3 LTS | ProjectVersion.txt pins 6000.3 line; exact revision resolves on first Hub open |
| URP project | URP package pinned + editor menu assigns pipeline asset on first open |
| Android/iOS build profiles | Player settings via editor tool (`WalkGame/Setup/Apply Product Identity`); device build validation pending |
| .gitignore | Done |
| Assembly definitions | Core, Building, Gameplay, Activity, Persistence, Content, World, UI, Platform.Android, Platform.iOS, Editor, Tests.EditMode |
| Input System | Package pinned; active-input toggle in setup menu |
| Test assemblies | WalkGame.Tests.EditMode (+ standalone harness) |
| Bootstrap scene | `Assets/WalkGame/Core/Bootstrap.unity` |
| Service composition root | `App/GameHost.cs` |
| ClockService | `Core/ClockService.cs` (`IClock`, system/mutable/offset variants) |
| Save serializer abstraction | `Persistence/SaveAbstractions.cs` + JSON implementation |
| First local save repository | `Persistence/FileSaveRepository.cs` (atomic writes + backup recovery) |
| Logging wrapper | `Core/Logging.cs` |
| Debug/dev menu shell | `UI/DebugMenuController.cs` + App wiring (dev-build gated) |

## Phase 1 - Gray-box region and canonical state

RegionDefinition/RegionState/stable IDs implemented per DATA_MODEL.md; Ashfall Basin
authored content-as-code with integrity tests; buildings instantiate from state via
`RegionPresenter`/`BuildingActor`; region load/unload is single-scene MVP (both modes
in one loaded scene per WORLD_BUILDING_SYSTEM section 10); save schema v1 with
sequential migrator; fixtures covered by deterministic tests.
Gray-box terrain visuals are primitives pending first-editor-open art pass.

## Phase 2 - Restoration and builder loop

VitalityLedger, Debug provider (+1,000 steps), step-to-vitality conversion, project
definitions/prerequisites/transaction flow, ruin-to-restored visual swap (gray box),
builder camera (pan/pinch/tap), placement grid with footprint+reserved-area validation,
move preview/confirm/cancel transaction, persisted placement - all implemented and
covered by tests including save/reload placement identity.

## Phase 3 - Explore mode

Third-person controller + follow camera, explicit Builder<->Explore mode state machine,
spawn at authored explore spawn, same canonical state for both views, boundary clamping.
NPC presence/lore interactions exist as domain state + unlock events; scene-side NPC
actors and ambience are stubs for the art pass.

## Phase 4 - Native activity integration

Debug provider covers all required debug controls. Android Kotlin bridge +
`AndroidStepSensorProvider` (cumulative counter, reboot re-baseline, permission states)
and iOS Core Motion bridge + `IosCoreMotionProvider` (7-day bounded history, live
session facts) are implemented behind platform asmdefs. Expedition session plumbing,
trust scoring and capped bonuses are domain-tested. **Device validation checklist not
yet run** (permission flows, reboot/resume behavior on hardware).

## Phase 5 - Idle/incremental systems

ProductionService with checkpoint math, offline cap (8h), backward-clock clamp,
per-producer storage caps, tier multipliers, collection API - deterministic tests pass.
Collection UI surfaces through the HUD resource readout and dedicated per-producer
collect buttons (`HudController` collect bar + `ProductionService.GetPendingCollectables`
/ `CollectAll`), refreshed on the activity ticker cadence.

## Phase 6 - Vertical-slice content

Ashfall Basin: 15 restoration projects across micro/building/ecosystem/landmark
categories, 9 building instances (water station, greenhouse, workshop, research hall,
three houses, dead grove, transit gate landmark), 4 producers, 3 NPCs, 5 lore objects,
milestone ladder, stage thresholds 0-3 with scripted full-playthrough test reaching the
transit-gate finale. Visual stages are gray-box tints pending art/audio.

## Phases 7-9

Not started by design: performance/privacy hardening requires physical devices;
playtest and post-MVP expansion follow the roadmap gates.
