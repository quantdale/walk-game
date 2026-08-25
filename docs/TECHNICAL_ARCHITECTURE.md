# Walk Game — Technical Architecture

## 1. Architecture goals

The architecture must support:

- iOS and Android.
- 3D mobile rendering.
- Builder and third-person views over the same region state.
- Offline-first progression.
- Native step/activity providers.
- Deterministic save/load.
- Contained regions loaded independently.
- Later cloud sync without rewriting core gameplay.
- Agent-friendly modular code.

## 2. Recommended stack

### Engine
- **Unity 6.3 LTS**.
- C#.
- Universal Render Pipeline (URP).
- Unity Input System.
- Addressables when content size justifies it.

### Native platform bridges
- iOS: Swift/Objective-C bridge for Core Motion and optional Core Location.
- Android: Kotlin/Java bridge for SensorManager and optional Fused Location Provider.

### Persistence
MVP:
- JSON or binary/MessagePack-style local save behind a repository abstraction.
- Atomic save writes.
- Backup previous known-good save.

Later:
- Cloud save adapter.
- Server-side reward ledger only if competition/social features require authority.

## 3. Top-level modules

Recommended Unity assembly/module boundaries:

```text
WalkGame.Core
WalkGame.Gameplay
WalkGame.World
WalkGame.Building
WalkGame.Activity
WalkGame.Persistence
WalkGame.Platform
WalkGame.UI
WalkGame.Content
WalkGame.Tests
```

Avoid a monolithic `GameManager` that owns every system.

## 4. Dependency direction

```text
UI → Application Services → Domain/Core
World Presentation → Domain/Core
Platform Adapters → Activity Abstractions
Persistence Adapters → Domain Save Models
```

The domain layer should not know about:
- UIKit.
- Android SDK.
- Unity scene objects where avoidable.
- HTTP/cloud implementation.

## 5. Core services

### `GameSession`
Owns current loaded profile/session references.

### `ClockService`
Provides trusted application time abstraction.

Reasons:
- Offline production.
- Daily resets.
- Testability.
- Future server-time reconciliation.

Never scatter `DateTime.UtcNow` directly across gameplay code.

### `ActivityService`
Consumes one or more `IActivityProvider` implementations and produces normalized activity deltas.

### `VitalityLedger`
Only component allowed to credit/spend Vitality.

Suggested API:

```csharp
Credit(VitalityCredit credit)
TrySpend(VitalitySpend spend)
GetBalance()
```

Record reason codes for debugging and future cloud reconciliation.

### `SaveService`
Serializes canonical profile state.

### `RegionService`
Loads region definition/state and coordinates presentation.

### `RestorationService`
Validates prerequisites and commits restoration project state.

### `BuildingPlacementService`
Validates and commits placement transforms.

### `ProductionService`
Calculates passive/offline production from timestamps.

### Player-facing presentation services

The Ashfall slice adds thin presentation services without moving authority out of the
canonical/domain boundary:

- `RegionEnvironmentPresenter` builds the reusable procedural basin kit once and derives
  river, grove, wetland, gate, lighting, atmosphere, particles, and stage accents from
  `RegionState`.
- `ExplorationService` owns lore discovery mutations; `NpcActor` and `LoreActor` are
  scene projections only.
- `ExpeditionController` observes provider tasks from coroutines and sends the one active
  session result through `ActivityService`; it never creates a second reward path.
- `FeedbackController` centralizes optional audio/haptic cues and persisted master/music/
  effects/reduced-motion settings. Missing clips do not affect gameplay comprehension.
- `HudController` and `ProjectPanelController` consume `UiContext` view delegates. They
  use safe-area roots, responsive CanvasScaler anchors, reusable rows, and contextual
  permission/save/empty states rather than exposing native error strings.

## 6. Scene architecture

### Persistent bootstrap scene

Contains:
- Application root.
- Service composition root.
- Global UI shell.
- Audio controller.
- Save/session controller.
- Region loader.

### Region scenes

Each region scene is content, not global logic.

Contains:
- Terrain.
- Environment.
- Placement masks.
- Static landmark anchors.
- Lighting profiles.
- Spawn points.
- Navigation data.

### Mode layers

Builder and Explore modes should preferably be different controllers/cameras inside the same region scene for MVP.

This minimizes:
- Reloading.
- Transform sync bugs.
- Duplicate region authoring.

If memory later requires separate scenes, both must still reconstruct from the same `RegionState`.

## 7. Mode state machine

Use an explicit state machine:

```text
Boot
  ↓
MainMenu / WorldMap
  ↓
LoadingRegion
  ↓
BuilderMode ↔ ExploreMode
  ↓
WorldMap
```

Do not infer mode from which camera happens to be enabled.

## 8. Activity abstraction

```csharp
public interface IActivityProvider
{
    Task<ActivityCapability> GetCapabilityAsync();
    Task<ActivitySnapshot> ReadSnapshotAsync(ActivityCursor cursor);
    Task StartSessionAsync(ActivitySessionConfig config);
    Task<ActivitySessionSample> PollSessionAsync();
    Task<ActivitySessionResult> StopSessionAsync();
}
```

Implementations:
- `DebugActivityProvider`
- `IosCoreMotionProvider`
- `AndroidStepSensorProvider`
- later `IosHealthKitProvider`
- later `AndroidHealthConnectProvider`

Game reward code must never call platform-native APIs directly.

## 9. Activity normalization pipeline

```text
Native provider data
  ↓
Provider-specific normalization
  ↓
Activity deduplication/reconciliation
  ↓
Plausibility/trust analysis
  ↓
Reward calculation
  ↓
VitalityLedger credit
  ↓
Persist cursor + ledger transaction atomically
```

The last two operations must not allow step credit without advancing the sync cursor, or duplicate rewards can occur after crashes.

## 10. Transaction principle

Operations that change currency and progression should be atomic at application level.

Example restoration transaction:

```text
Validate project
Validate Vitality/resources
Create transaction
Deduct cost
Mark project complete
Apply building/environment state
Persist save
Publish domain events
```

If persistence fails, rollback in-memory transaction or reload last known-good state.

## 11. Domain events

Use simple typed domain events rather than hard wiring systems together.

Examples:
- `VitalityCredited`
- `ProjectCompleted`
- `BuildingRestored`
- `BuildingMoved`
- `RegionStageChanged`
- `RegionUnlocked`
- `ActivityMilestoneReached`

UI and audio can subscribe without being embedded in core logic.

Avoid adding a heavyweight event bus framework until needed.

## 12. Data definitions

Use ScriptableObjects for immutable content definitions:
- Region definitions.
- Building definitions.
- Restoration projects.
- Upgrade curves.
- Resource definitions.
- Milestone definitions.

Never use ScriptableObjects as runtime player-save storage.

## 13. ID policy

All persistent entities require stable string/GUID IDs.

Examples:

```text
region.ashfall
building.greenhouse.small
project.ashfall.restore_pump
landmark.ashfall.transit_gate
resource.vitality
```

Never persist Unity instance IDs.

IDs must not change after shipping unless a save migration maps old → new.

## 14. Save versioning

Every save payload carries:

```text
schemaVersion
```

`schemaVersion` is the only contract field: it gates forward-schema refusal and
migration (see below). The serialized profile itself is the canonical document —
identity/metadata fields such as app version or creation timestamps are not part
of the current v1 schema (see `DATA_MODEL.md §2` for the authoritative field
list); adding one is a schema change requiring migration, tests, and a copier
extension per ADR 0007.

On load:

```text
if schemaVersion < current:
    migrate sequentially
```

Keep each migration deterministic and testable.

## 15. Save strategy

Use atomic local writes:

1. Serialize new save to temporary file.
2. Validate serialization.
3. Rename current save to backup.
4. Replace current with temp.
5. Keep at least one last-known-good backup.

The repository uses an `ISaveFileSystem` seam around these operations so interruption,
write failure, and backup-rotation cases can be tested without changing the production
algorithm. Save validation receives the trusted `IClock`; future timestamps are reported
as anomalies and preserved for recovery rather than silently rewritten.

Rotation is trust-checked (ADR 0007): the main slot is read back before it becomes the
next backup. A corrupt main is quarantined byte-for-byte and the validated payload seeds
the backup first; a forward-schema main refuses rotation entirely. At every injected
interruption point at least one trustworthy copy of the last-known-good profile survives.

On corruption:
- Try backup.
- Never silently reset player world if a backup exists.

Application-level persistence health (ADR 0007) gates everything above: only an empty
save directory auto-creates a profile, fatal load states boot fail-closed with no
mutation surface or lifecycle autosave, and player-visible mutations commit
transactionally - a failed write reverts canonical state to exact disk truth instead of
diverging from it.

## 16. Offline production architecture

Do not run timers while app is closed.

Persist:
- `lastProductionCheckpointUtc` per producer or aggregate production system.

On resume:

```text
elapsed = clamp(now - checkpoint, 0, offlineCap)
production = Calculate(elapsed, currentBuildingState)
checkpoint = now
```

Handle backward device clock changes by clamping negative elapsed to zero and logging the anomaly.

## 17. Building presentation

Recommended component split:

```text
BuildingActor
├── BuildingIdentity
├── BuildingVisualController
├── BuildingInteraction
├── PlacementFootprint
└── optional ProductionIndicator
```

`BuildingActor` reads immutable definition + mutable building state.

Presentation must never become the authoritative source of placement.

## 18. Region presentation

```text
RegionPresenter
├── loads RegionDefinition
├── reads RegionState
├── instantiates/updates BuildingActors
├── updates environment stage
├── coordinates NPC/landmark presentation
└── listens to domain events
```

The implementation adds a `RegionEnvironmentPresenter` child and world-actor projection
layer to this contract. `BuildingActor` still projects canonical grid placement and
lifecycle; placement previews are explicitly transient and are committed only by
`BuildingPlacementService`.

## 19. Native iOS bridge

Use a narrow bridge surface.

Example conceptual calls:

```text
WG_IsPedometerAvailable()
WG_RequestMotionPermission()
WG_QueryPedometer(startUnix, endUnix)
WG_StartPedometerUpdates(startUnix)
WG_StopPedometerUpdates()
```

Return structured JSON or invoke C# callbacks through a stable wrapper.

Wrap native calls behind `UNITY_IOS && !UNITY_EDITOR`.

## 20. Native Android bridge

Use a Unity Android library/plugin or `AndroidJavaObject` wrapper around a small Kotlin module.

Responsibilities:
- Motion permission handling.
- Step sensor registration.
- Cumulative counter persistence handoff.
- Session location sampling when enabled.

Do not place reward/business logic in Kotlin. Native module returns sensor facts; C# domain decides rewards.

## 21. Location policy in architecture

Precise location is optional and used only for explicitly started activity sessions that need verification/distance.

Normal step earning:
- Must work without GPS.

If location permission is denied:
- Session may use steps/time only.
- Performance bonus may be limited.
- Base steps still count.

## 22. Content loading

Use Addressables when multiple 3D regions materially increase install size.

Suggested labels:

```text
region_ashfall
region_coast
shared_buildings
shared_nature
```

Region load flow:

```text
Show transition UI
↓
Persist outgoing region state
↓
Unload current addressable region
↓
Load target region async
↓
Instantiate from RegionState
↓
Activate scene
```

## 23. Performance strategy

Target physical-device profiling from the first region.

Priorities:
- URP mobile profile.
- Baked lighting where possible.
- GPU instancing for repeated vegetation/props.
- LODs on buildings and vegetation.
- Object pooling for repeated effects.
- Async region loading.
- Avoid per-frame LINQ/allocations in hot loops.
- Keep NPC AI simple.

The builder camera can reveal more of the region than third-person view, so profile both separately.

## 24. Testing layers

### EditMode/unit tests
- Reward formulas.
- Vitality ledger.
- Restoration prerequisites.
- Placement grid validation.
- Offline production.
- Save migrations.
- Step reconciliation.

### PlayMode tests
- Builder placement.
- Mode transitions.
- Region load/unload.
- State rehydration.

### Device tests
- Native pedometer permission.
- Step deltas.
- Background/resume.
- Reboot/counter reset.
- Location denied/allowed.
- Battery behavior.

## 25. CI goals

When code exists, CI should eventually run:
- C# formatting/static analysis.
- EditMode tests.
- PlayMode tests where feasible.
- Build validation for Android.
- iOS project generation validation.

Do not block initial prototype on complex CI, but establish tests before adding many regions.

## 26. Backend insertion point

Later backend services should implement interfaces such as:

```text
ICloudSaveRepository
IAccountService
IRemoteConfigService
IEventService
```

Core game should continue using local domain state if network is unavailable.

## 27. Architecture anti-patterns

Reject PRs that introduce:

- A global singleton accessing every system.
- Direct native sensor access from UI.
- Reward logic in platform code.
- Player save values inside ScriptableObjects.
- Building transform state stored only in scene hierarchy.
- Region-specific logic hardcoded into generic systems.
- Unversioned save schemas.
- GPS requirement for normal steps.
- Per-frame offline production simulation.

## 28. Definition of architecture-complete vertical slice

The architecture is validated when:

1. Debug provider adds steps.
2. Vitality credits once.
3. Player restores building.
4. Player moves building.
5. Save/restart persists exact state.
6. Explore mode shows same arrangement.
7. Offline production computes correctly.
8. Region unload/reload rehydrates correctly.
9. Native Android step provider replaces debug provider without game logic changes.
10. Native iOS step provider replaces debug provider without game logic changes.
