# Walk Game

A mobile restoration-builder where **real-world movement restores and advances a dead world**.

The player begins in gray, ruined regions. Walking generates **Vitality**, which is spent to restore ecosystems, repair buildings, rebuild settlements, and eventually advance the world beyond its former civilization. Restored buildings can be rearranged in a bird's-eye builder view, and the player can enter the same region in third-person Explore mode to walk through what they rebuilt.

This repository now contains a **complete vertical-slice implementation of the Ashfall
Basin region** (Phases 0-6 systems, content-as-code) with 62 passing domain tests. See
[`docs/IMPLEMENTATION_STATUS.md`](docs/IMPLEMENTATION_STATUS.md) for the phase-by-phase
state and what still requires an editor or physical devices.

## Quick start

**Verify the domain (no Unity required):**

```bash
dotnet test verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj
```

The harness compiles the exact same engine-free sources as the Unity assemblies
([ADR 0001](docs/adr/0001-standalone-verification-harness.md)).

**Open in Unity (first time):**

1. Open the project in Unity 6.3 LTS (Hub resolves `ProjectSettings/ProjectVersion.txt`).
2. Wait for import/compile.
3. Run `WalkGame > Setup > Configure URP and Input System`, then
   `WalkGame > Setup > Apply Product Identity` once.
4. Run `WalkGame > Validate Content IDs` to sanity-check authored content.
5. Open `Assets/WalkGame/Core/Bootstrap.unity` and press Play.

## Start here

1. [`docs/MASTER_PLAN.md`](docs/MASTER_PLAN.md) — canonical product direction and scope.
2. [`docs/ROADMAP.md`](docs/ROADMAP.md) — implementation phases and milestone acceptance criteria.
3. [`docs/TECHNICAL_ARCHITECTURE.md`](docs/TECHNICAL_ARCHITECTURE.md) — Unity/mobile architecture and system boundaries.
4. [`AGENTS.md`](AGENTS.md) — repository-wide rules for coding agents.

## Design documentation

- [`docs/GAME_DESIGN.md`](docs/GAME_DESIGN.md) — progression, restoration, economy, idle systems, NPCs, and content.
- [`docs/WORLD_BUILDING_SYSTEM.md`](docs/WORLD_BUILDING_SYSTEM.md) — contained regions, building restoration/placement, and Builder/Explore synchronization.
- [`docs/ACTIVITY_REWARD_SYSTEM.md`](docs/ACTIVITY_REWARD_SYSTEM.md) — walking, running, endurance, capped bonuses, and movement reward formulas.

## Engineering documentation

- [`docs/TECHNICAL_ARCHITECTURE.md`](docs/TECHNICAL_ARCHITECTURE.md) — recommended Unity 6.3 LTS architecture.
- [`docs/DATA_MODEL.md`](docs/DATA_MODEL.md) — canonical save/runtime data model and migration rules.
- [`docs/MOBILE_ACTIVITY_INTEGRATION.md`](docs/MOBILE_ACTIVITY_INTEGRATION.md) — Core Motion, Android step sensors, active Expeditions, and optional HealthKit/Health Connect integration.
- [`docs/TESTING_AND_PERFORMANCE.md`](docs/TESTING_AND_PERFORMANCE.md) — automated/device test plan and mobile performance strategy.
- [`docs/PRIVACY_SAFETY_ANTI_CHEAT.md`](docs/PRIVACY_SAFETY_ANTI_CHEAT.md) — privacy, movement safety, anti-cheat, and store-policy guardrails.
- [`docs/RESEARCH_NOTES.md`](docs/RESEARCH_NOTES.md) — August 2026 research findings with official implementation references.

## Agent/contributor workflow

- [`docs/AGENT_EXECUTION_GUIDE.md`](docs/AGENT_EXECUTION_GUIDE.md) — implementation workflow, architecture invariants, test expectations, and ADR process.

## Fixed product decisions

The current plan intentionally commits to these constraints:

- Steps are the universal movement baseline.
- Walking generates Vitality; Vitality drives restoration.
- Running/endurance receive capped optional bonuses, not unlimited speed multipliers.
- Passive step earning does **not** require GPS.
- Rest does not destroy progress.
- The world is made of contained regions rather than one seamless open world.
- Travel between regions uses a world map/transit mechanism.
- Each region has a bird's-eye Builder View and third-person Explore View.
- Both views use one canonical region state, so moved buildings appear in the exact same location in either mode.
- Restored buildings can generate idle/passive resources.
- The first development target is one polished vertical-slice region.

## Recommended implementation direction

- Unity 6.3 LTS
- C#
- Universal Render Pipeline
- Offline-first local save
- Native phone pedometer APIs for MVP
  - iOS Core Motion `CMPedometer`
  - Android `TYPE_STEP_COUNTER`
- Optional location only for explicitly started active Expeditions
- HealthKit / Health Connect considered later for wearable/history imports after privacy and store-policy review

## Current roadmap target

Phases 0-6 are implemented at vertical-slice level (one region, content-as-code, gray-box
visuals) and verified by the domain test suite; native providers await on-device
validation. Next gates, in order: first editor open + Play validation (Phase 0
acceptance), physical Android/iOS device passes for Phase 4/7 acceptance criteria.

Architecture decisions made during implementation are recorded under
[`docs/adr/`](docs/adr).