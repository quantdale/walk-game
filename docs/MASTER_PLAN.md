# Walk Game — Master Plan

> Working title only. This document is the canonical product and development plan. If another document conflicts with this one, this file wins unless an ADR explicitly supersedes it.

## 1. Product thesis

**Walk Game is a mobile restoration-builder where real-world movement creates the energy that revives a dead world.**

The player begins with a gray, lifeless region full of damaged landmarks, ruined buildings, dry water systems, dead soil, and dormant infrastructure. Real-world movement generates the primary progression resource. The player spends that resource to restore the region, unlock structures, revive ecosystems, rebuild settlements, and eventually advance the world beyond its original state.

The emotional promise is not "earn coins by walking." It is:

> **What you move in real life becomes visible, permanent progress in another world.**

The game combines five pillars:

1. **Movement as progression** — steps are the universal baseline.
2. **Restoration** — every major spend creates visible environmental change.
3. **Incremental/idle growth** — restored systems continue producing while the player is away.
4. **Light city building** — restored buildings can be repositioned, upgraded, and arranged.
5. **Embodied payoff** — the player can switch from bird's-eye builder mode into third-person exploration of the same region.

## 2. Core player loop

```text
MOVE IN REAL LIFE
      ↓
Generate Vitality + optional activity bonuses
      ↓
Open the game / collect idle output
      ↓
Choose restoration project
      ↓
World visibly changes
      ↓
Building/ecosystem begins producing
      ↓
Unlock new project, district, landmark, or region
      ↓
Arrange / upgrade / decorate region
      ↓
Explore the restored region in third person
      ↓
Discover story, secrets, NPCs, and future goals
      ↺
```

The loop must work at three time scales:

- **60 seconds:** collect, restore one small object, move one building, inspect progress.
- **1 day:** meaningful progress from normal daily movement.
- **Weeks/months:** complete districts, unlock regions, advance world eras.

## 3. World progression model

The world advances through macro eras rather than ending at "100% restored."

### Era 0 — The Silent World
- Gray terrain.
- Dry or polluted water.
- No active infrastructure.
- Ruined buildings are visible but unusable.
- Minimal ambient life.

### Era 1 — Recovery
- Soil, water, moss, grass, small plants.
- Basic restoration projects.
- First working buildings.
- Small fauna and weather changes.

### Era 2 — Rewilding
- Forests, wetlands, coastlines, wildlife.
- Ecological chains begin producing passive resources.
- New ruins become discoverable as vegetation and water systems recover.

### Era 3 — Resettlement
- NPCs return.
- Farms, workshops, homes, transport nodes.
- Player begins arranging the region as a settlement rather than only repairing it.

### Era 4 — Flourishing Civilization
- Town systems become interconnected.
- Services, culture, logistics, production, research.
- Strong city-builder layer, still intentionally lighter than Cities: Skylines.

### Era 5 — Beyond Restoration
- The world advances beyond the old civilization.
- Eco-technology, living architecture, high-efficiency transport, advanced restoration engines, floating gardens, climate engineering, etc.
- Endgame becomes creation, not repair.

## 4. Region model

The world is divided into **self-contained regions**. Regions are not physically traversable from one another in third-person mode.

Each region contains:

- A fixed terrain/landscape shell.
- A bounded playable third-person space.
- A builder placement grid or nav-safe placement zones.
- 8–30 meaningful building/landmark slots at MVP scale.
- Restoration stages.
- A region-specific ecosystem/resource identity.
- A signature landmark.
- Story discoveries.
- Passive production once restored.

Travel between regions occurs using the **World Map / Transit Gate / restored transport system** rather than walking across a continuous open world.

This is a deliberate scope constraint. Do not turn the project into a seamless open-world game.

## 5. Two-view region architecture

Every region has two presentation modes backed by **one canonical RegionState**.

### Builder View
- Bird's-eye / angled city-builder camera.
- Select, rotate, move, and decorate restored buildings.
- Start restoration projects.
- Inspect production chains and region stats.

### Explore View
- Third-person character controller.
- Walk through the current saved arrangement.
- Interact with landmarks and NPCs.
- Discover story objects and environmental details.
- No cross-region traversal.

### Non-negotiable rule

A building moved in Builder View must appear in exactly the corresponding position in Explore View.

Do **not** maintain two independently-authored versions of a region. Both views must deserialize the same placement/state data.

## 6. Movement economy

### Base rule

Steps generate **Vitality**.

Do not treat raw steps as premium currency. Vitality is the bridge between movement and the game economy.

Recommended initial tuning:

```text
1 verified step = 1 Vitality before caps/modifiers
```

This ratio is intentionally simple for prototyping. Economy balancing comes later.

### Activity styles

The game supports different movement styles without creating a dangerous speed arms race:

- **Walker** — steps and consistency.
- **Explorer** — sustained distance.
- **Runner** — verified active-session distance and moving time.
- **Tempo** — moderate sustained pace/cadence.
- **Endurance** — longer sessions.
- **Recovery** — low-intensity/rest-day engagement is never penalized.

There is **no uncapped reward for maximum speed**.

Running/sprint bonuses must be capped and based on safe verified sessions, not "faster = infinitely more reward."

See `ACTIVITY_REWARD_SYSTEM.md`.

## 7. Idle/incremental layer

Walking is the ignition; restored systems provide momentum.

Examples:

- Restored spring → Water per hour.
- Greenhouse → Biomass per hour.
- Workshop → Parts per hour.
- Research tower → Knowledge per hour.
- Town square → Morale/culture generation.

Rules:

- Offline production has a configurable cap (initial target: 8–12 hours).
- Steps should accelerate or unlock progression, not be replaced by passive production.
- Idle output should never make real-world movement irrelevant.
- No negative offline decay that punishes inactivity.

## 8. Restoration projects

A restoration project is the fundamental content unit.

Every project has:

- `projectId`
- Region
- Prerequisites
- Vitality cost
- Optional resource costs
- Duration or instant completion behavior
- Visual stage transition
- Reward/unlock set
- Narrative text

Example chain:

```text
Clear Aqueduct Rubble
  → Restore Pump Station
      → River begins flowing
          → Restore Wetland
              → Wildlife returns
                  → Old Riverside District becomes buildable
```

The player should understand *why* one restoration enables another.

## 9. Building system

Buildings begin as authored ruins in predefined world locations.

Flow:

1. Ruin visible from first visit.
2. Player reaches prerequisite.
3. Player spends Vitality/resources.
4. Restoration animation / staged material swap.
5. Building becomes functional.
6. Building becomes movable within allowed placement area.
7. Player can upgrade and decorate.

MVP building placement should use constrained footprints/grid snapping. Avoid fully freeform arbitrary physics placement.

## 10. Story premise

Keep lore flexible until gameplay validates, but the working premise is:

> The world did not simply collapse; its systems stopped sustaining life. The player possesses or is linked to a mechanism that converts human movement into restorative energy. As regions recover, ruins reveal why the old world failed and whether rebuilding it exactly as it was would repeat the same mistake.

Story principles:

- Mystery is uncovered through restoration.
- The environment tells the story before exposition does.
- The world itself is the protagonist.
- Late game asks the player to build something better, not merely reconstruct the past.

## 11. Technical direction

### Engine

**Unity 6.3 LTS + C# + URP** is the default production recommendation.

Reasons:

- Mature iOS/Android deployment pipeline.
- Strong mobile profiling/tooling.
- Native plug-in path for Core Motion / Android sensors.
- Addressables support for loading contained region scenes/content on demand.
- Strong fit for a project mixing third-person 3D and builder UI.

Do not upgrade Unity minor versions casually once production starts. Record engine upgrades as ADRs.

### Platform activity strategy

#### MVP

Use local phone sensors first:

- iOS: Core Motion `CMPedometer`.
- Android: `Sensor.TYPE_STEP_COUNTER` / `TYPE_STEP_DETECTOR` with `ACTIVITY_RECOGNITION` permission.
- Active running sessions: location + time + cadence/steps where available.

This keeps the first playable build independent of HealthKit/Health Connect approval and wearable complexity.

#### Post-MVP optional integration

- Apple HealthKit for wearable/imported step and workout support.
- Android Health Connect for wearable/imported steps/workouts.

Health platform integrations require explicit privacy/policy review before shipping.

See `MOBILE_ACTIVITY_INTEGRATION.md` and `PRIVACY_SAFETY_ANTI_CHEAT.md`.

## 12. Backend philosophy

**Offline-first, server-optional for prototype.**

For the first vertical slice:

- Local save is authoritative.
- No account required.
- No social leaderboard.
- No cloud economy dependency.

Add backend only when needed for:

- Cloud save.
- Account recovery.
- Cross-device sync.
- Live events.
- Server-authoritative rewards/anti-cheat for competitive systems.

Never block basic walking/restoration gameplay on network availability.

## 13. MVP definition

The MVP is **one polished region**, not a tiny version of the full world.

### MVP region must support

- Step ingestion.
- Vitality generation.
- 10–15 restoration projects.
- 6–10 restorable buildings.
- 3–5 passive resource producers.
- Builder camera.
- Building repositioning.
- Save/load.
- Third-person explore mode.
- Same building arrangement visible in both modes.
- 1 landmark chain.
- 1 small story arc.
- Offline production.
- Basic suspicious-activity filtering.
- Android and iOS device build.

### Explicit MVP exclusions

- Multiplayer.
- Guilds.
- PvP.
- Global leaderboards.
- Seamless open world.
- Dozens of regions.
- Complex traffic simulation.
- Fully simulated citizens.
- Real-time economy server.
- User-generated structures.
- Procedural city generation.
- Combat unless later validated as essential.

## 14. Success criteria

The vertical slice succeeds if a tester can say all three:

1. **"I wanted to walk because I was close to restoring something."**
2. **"The region visibly became mine over time."**
3. **"Walking through the city I built felt rewarding."**

Technical success targets:

- Stable 30 FPS minimum on target low/mid device; 60 FPS where supported.
- Region load target under 5 seconds on target mid-range device after first download.
- Save corruption recovery path.
- No step duplication after restart/time-zone changes.
- Offline progress deterministic and bounded.
- Normal play possible without location permission; location only required for optional verified active sessions.

## 15. Product guardrails

Do not add mechanics that:

- Remove progress because the player rested.
- Reset a long-term walking streak to zero after one missed day.
- Reward unlimited high speed.
- Require unsafe movement while looking at the screen.
- Encourage phone interaction during running.
- Require precise location for ordinary step earning.
- Sell or target ads using movement/health data.
- Turn restoration into a generic coin-shop reskin.

## 16. Document map

- `GAME_DESIGN.md` — detailed loops, economy, progression, content.
- `WORLD_BUILDING_SYSTEM.md` — region, buildings, restoration, builder/explore synchronization.
- `ACTIVITY_REWARD_SYSTEM.md` — walking/running reward design and formulas.
- `TECHNICAL_ARCHITECTURE.md` — Unity architecture, scenes, services, boundaries.
- `DATA_MODEL.md` — canonical runtime/save schemas.
- `MOBILE_ACTIVITY_INTEGRATION.md` — iOS/Android implementation guidance.
- `PRIVACY_SAFETY_ANTI_CHEAT.md` — safety, privacy, fraud resistance, policy risks.
- `ROADMAP.md` — phased implementation plan.
- `AGENT_EXECUTION_GUIDE.md` — instructions for coding agents working in this repo.
- `RESEARCH_NOTES.md` — source-backed implementation research.

## 17. First implementation order

1. Bootstrap Unity project.
2. Implement canonical data model.
3. Build one gray region scene.
4. Implement builder camera and placement.
5. Implement restoration state transitions.
6. Implement save/load.
7. Implement third-person exploration using the same RegionState.
8. Implement fake/debug activity provider.
9. Integrate iOS/Android step providers.
10. Implement Vitality ledger and reward normalization.
11. Implement idle production.
12. Add landmark/story chain.
13. Profile on physical mid-range Android and iPhone devices.
14. Only then add more regions/content.

---

**Status:** Product direction fixed enough to begin vertical-slice development. All numerical balance values remain provisional until playtesting.