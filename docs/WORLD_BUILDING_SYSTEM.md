# Walk Game — World & Building System

## 1. Purpose

This document defines how regions, restoration, building placement, and the dual Builder/Explore presentation work together.

The central rule is simple:

> **There is one world state and multiple views of it.**

Builder View and Explore View must never maintain separate copies of building positions or restoration progress.

## 2. Region composition

Each region is a bounded content package with:

- `RegionDefinition` — static authored data.
- `RegionState` — mutable player-owned state.
- Terrain/landscape scene.
- Building anchors / placement area.
- Restoration visuals.
- Navigation data.
- NPC spawn definitions.
- Audio/lighting profiles by restoration stage.
- Region-specific content catalog.

### Static versus dynamic data

**Static authored data** belongs in ScriptableObjects/content definitions:
- IDs.
- Costs.
- Prefab references.
- Footprints.
- Upgrade definitions.
- Unlock prerequisites.
- Default ruin transforms.

**Dynamic player data** belongs in save state:
- Restored/unrestored.
- Current building tier.
- Player-selected transform.
- Production timestamps.
- Completed projects.
- Discovered story objects.

Never serialize whole Unity GameObjects as save data.

## 3. Coordinate strategy

Use local region coordinates.

Every placed building stores:

```text
regionId
buildingInstanceId
buildingDefinitionId
positionLocal
rotationY
upgradeTier
state
```

Do not store world-space transforms that depend on scene origin. This makes regions portable and avoids transform drift if scenes are reorganized.

## 4. Placement model

### Recommended MVP approach

Use a hybrid **grid + authored placement mask**.

- Region contains one or more legal placement polygons/volumes.
- Buildings snap to configurable grid size.
- Rotation is quantized, initially 90° increments.
- Footprint occupancy is checked before placement commits.
- Landmarks can have locked positions.
- Roads, cliffs, water, gates, and player spawn zones are blocked cells.

Why not full free placement?
- Easier collision validity.
- Easier NPC navigation.
- Easier touch controls.
- Better deterministic save/load.
- Lower risk of impossible city layouts.

## 5. Placement transaction

Moving a building should be transactional.

```text
BeginMove(buildingId)
  → cache original transform
  → preview ghost at candidate position
  → validate footprint/nav constraints
  → Confirm = commit RegionState
  → Cancel = restore original transform
```

Never write to the canonical save state continuously while the player drags a building.

## 6. Placement validation

A candidate placement is valid if:

- Entire footprint is inside placement mask.
- No occupied footprint overlap.
- Not in reserved landmark/road/water cells.
- Terrain slope is below building limit.
- Required access point remains reachable if implemented.
- Explore-mode player spawn is not obstructed.

For MVP, do not dynamically validate a complete traffic simulation.

## 7. Ruin → restored lifecycle

Each building has visual stages:

```text
Ruin
  ↓
Cleared / Under Restoration
  ↓
Restored Tier 1
  ↓
Improved Tier 2
  ↓
Flourishing Tier 3
```

Implementation options:

### Preferred
A root `BuildingActor` with child visual variants enabled/disabled by state.

Advantages:
- Stable IDs.
- Stable interaction collider root.
- Easy stage swaps.
- Easy pooling.

### Alternative
Swap entire prefabs using the same canonical building instance data.

Use only if art complexity makes child variants unwieldy.

## 8. When movement becomes available

A ruin is initially authored at a fixed transform.

After Tier 1 restoration:
- Its original position becomes the initial player placement.
- It can be moved if `movableAfterRestore = true`.
- Moving it does not alter project completion.
- Moving it does not reset production.

Certain structures remain fixed:
- Dams.
- Bridges.
- Transit gates.
- Major landmarks integrated into terrain.

## 9. Builder View

### Camera

Use a perspective or orthographic-isometric camera with:
- Pan.
- Pinch zoom.
- Rotate region camera if desired.
- Tap select.
- Drag building ghost.

### UI hierarchy

- Top: region status, Vitality, key resources.
- Bottom: selected object actions.
- Context panel: restore / upgrade / move / info.
- Map button: leave region.
- Explore button: enter third person.

Do not cover the region with permanent dashboard UI.

## 10. Explore View

Explore View loads the same region visual content plus:
- Third-person player.
- NPCs.
- interaction prompts.
- exploration-only effects.

### Enter flow

```text
Save current RegionState
↓
Disable builder input/UI
↓
Ensure placed buildings are instantiated from RegionState
↓
Bake/update runtime navigation if required OR use placement constraints that preserve authored nav
↓
Spawn character at safe Explore spawn
↓
Enable third-person camera/controller
```

### Exit flow

```text
Disable character
↓
Save discovery/interactions
↓
Enable builder camera/UI
```

For the MVP, both modes may live in the same loaded scene and toggle presentation layers. This minimizes load time and synchronization complexity.

## 11. Navigation strategy

Dynamic building placement makes NPC navigation the hardest part of the dual-view concept.

### MVP recommendation

Design placement zones so the main pedestrian network remains outside building footprints.

Use:
- Authored walkable roads/paths.
- Building setbacks.
- Fixed nav corridors.
- Short local NPC roaming zones.

This avoids needing to fully rebake navigation every time a player moves a building.

### Later option

Use runtime NavMesh updates for movable obstacles when larger free-placement areas are added.

Do not begin with arbitrary freeform city streets and dynamic traffic.

## 12. Region streaming

Each region is independent and should be loadable/unloadable.

Recommended scene structure:

```text
Bootstrap (persistent)
├── Services
├── UI shell
├── Audio manager
├── Save/session
└── Region loader

Region_Ashfall (Addressable)
├── Terrain
├── Static landmarks
├── Placement masks
├── Building anchors
├── Lighting profiles
└── Nav data
```

Use Addressables for region scenes/content when content volume grows. Unity supports asynchronous Addressable scene loading and delayed activation, which is useful for region transitions.

## 13. Region travel

Third-person players cannot physically walk from Region A to Region B.

Travel is handled through:
- World Map.
- Transit Gate.
- Airship/train/portal fiction.

The transition can show:
- Region summary.
- Lifetime walking distance.
- New-region preview.
- Loading screen.

This is an explicit product rule, not a temporary limitation.

## 14. Restoration visuals

The dead-to-living transition must be visible at several scales.

### Object scale
- Ruin repairs.
- Lights turn on.
- Water begins flowing.

### District scale
- Vegetation density.
- Road cleanliness.
- NPC population.
- Ambient VFX.

### Region scale
- Fog/tone shifts.
- Sky/lighting changes.
- Music instrumentation grows.
- Wildlife ambience returns.

Prefer authored staged transitions over expensive fully dynamic ecosystem simulation.

## 15. Material/lighting restoration stages

Define 4–6 regional visual states, for example:

```text
0 Dead
1 First Growth
2 Recovering
3 Rewilded
4 Resettled
5 Flourishing
```

A `RegionVisualController` interpolates or swaps:
- Volume profile.
- Directional light settings.
- Fog.
- Terrain material parameters.
- Vegetation groups.
- Water state.
- Ambient audio layers.

Do not tie every blade of grass directly to step count.

## 16. Building production

Each restored building may own a production component defined by data:

```text
resourceType
baseRatePerHour
storageCap
upgradeMultipliers
requiredInputs[]
```

Production is calculated from timestamps, not simulated every second while offline.

Example:

```text
elapsed = min(now - lastCollectedAt, offlineCap)
produced = ratePerHour * elapsedHours
```

Server time can replace device time later if competitive economy requires it.

## 17. Decorations

Decorations should be low-cost content with simple placement footprints.

Categories:
- Benches.
- Trees.
- Lamps.
- Planters.
- Signs.
- Sculptures.

Decorations should not affect core economy at MVP. Cosmetic objects are excellent candidates for later monetization because they do not corrupt movement fairness.

## 18. Region completion

A region should never become dead content after 100% restoration.

After core restoration:
- Era upgrades unlock.
- New cosmetic sets unlock.
- Advanced buildings replace selected restored ones.
- NPC population/activity increases.
- Landmark gets post-restoration projects.
- Region can participate in global world goals later.

## 19. Performance budgets

Initial target per loaded region on mid-range mobile:

- Keep active GameObjects conservative; pool repeated props.
- Prefer baked lighting where practical.
- Use URP.
- Use LOD Groups for large buildings/vegetation.
- Occlusion culling where it materially helps.
- Avoid dozens of real-time lights.
- Keep shader variants under control.
- Avoid per-frame allocations in placement and NPC systems.

Specific triangle/draw-call budgets must be measured on target devices rather than guessed.

## 20. Acceptance tests

A region implementation is not complete unless all pass:

1. Restore a building.
2. Move it in Builder View.
3. Close and reopen app.
4. Position persists.
5. Enter Explore View.
6. Building appears in exactly that position.
7. Player/NPC navigation is still valid.
8. Upgrade building.
9. Visual tier updates in both views.
10. Travel to another region and back.
11. State remains identical.
12. Offline production resumes from correct timestamp.

## 21. Scope warning

If development begins drifting toward any of the following, stop and re-evaluate:

- Simulated road traffic.
- Fully autonomous citizen economy.
- Seamless continent traversal.
- Destructible terrain.
- Arbitrary structural construction.
- Physics-based placement of every prop.

Those are separate games. The product value is restoration powered by real movement, not maximal city-simulation complexity.