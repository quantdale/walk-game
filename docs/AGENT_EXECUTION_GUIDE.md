# Walk Game — Coding Agent Execution Guide

## 1. Purpose

This file tells coding agents how to work in this repository without accidentally changing the product architecture.

Read in this order before implementing features:

1. `docs/MASTER_PLAN.md`
2. `docs/ROADMAP.md`
3. `docs/TECHNICAL_ARCHITECTURE.md`
4. The domain-specific document for the task.
5. `docs/DATA_MODEL.md`
6. `docs/PRIVACY_SAFETY_ANTI_CHEAT.md` for any activity/location/analytics work.

If a task conflicts with these documents, do not silently improvise around the conflict. Document the proposed change in the PR and update the affected design documentation.

## 2. Product invariants

Do not violate these unless an explicit architecture/product decision supersedes them:

1. Real-world movement generates the primary progression resource.
2. Steps are the universal baseline.
3. Running bonuses are capped; top speed is not an infinite multiplier.
4. Passive step earning must not require GPS.
5. Rest does not destroy player progress.
6. Regions are bounded and independently traveled via map/transit, not seamless open world.
7. Builder View and Explore View render one canonical `RegionState`.
8. A restored/moved building must persist and appear in the same place in Explore View.
9. Native platform code returns sensor facts; C# domain logic computes rewards.
10. Core gameplay remains offline-capable.

## 3. Current development target

The first target is **one complete vertical-slice region**.

Do not pre-emptively build:
- multiplayer.
- global leaderboards.
- seamless world streaming.
- traffic simulation.
- complex citizen AI.
- combat.
- live-ops backend.
- monetization store.

unless the issue specifically and deliberately changes the roadmap.

## 4. Task workflow

For each task:

### Step 1 — Locate milestone

Find the task in `ROADMAP.md`.

If absent, decide whether it belongs in the current milestone or is premature.

### Step 2 — Identify affected domain

Examples:

```text
Activity        → ACTIVITY_REWARD_SYSTEM.md + MOBILE_ACTIVITY_INTEGRATION.md
Buildings       → WORLD_BUILDING_SYSTEM.md
Persistence     → DATA_MODEL.md + TECHNICAL_ARCHITECTURE.md
Platform sensor → MOBILE_ACTIVITY_INTEGRATION.md + PRIVACY_SAFETY_ANTI_CHEAT.md
```

### Step 3 — Write/adjust tests first when feasible

High-value logic should have deterministic tests before or with implementation.

### Step 4 — Implement narrow vertical change

Avoid unrelated refactors.

### Step 5 — Validate invariants

Use the relevant acceptance tests in documentation.

### Step 6 — Update documentation

If implementation meaningfully changes architecture, public API, schema, permission behavior, or roadmap status, update the corresponding Markdown file in the same PR.

## 5. Branch/PR conventions

Suggested branch names:

```text
feat/save-core
feat/region-state
feat/restoration-projects
feat/builder-placement
feat/explore-mode
feat/android-step-provider
feat/ios-pedometer-provider
fix/step-dedup
refactor/activity-provider
```

Suggested commit messages:

```text
feat: add vitality ledger
fix: prevent duplicate step credit on resume
test: cover Android step counter reset
docs: update activity provider contract
```

Keep PRs reviewable. Prefer one coherent feature or bugfix per PR.

## 6. Unity project conventions

Once project exists, recommended structure:

```text
Assets/
  WalkGame/
    Core/
    Gameplay/
    World/
    Building/
    Activity/
    Persistence/
    Platform/
      iOS/
      Android/
    UI/
    Content/
    Tests/
      EditMode/
      PlayMode/
```

Third-party packages should not be mixed into `Assets/WalkGame/`.

## 7. Assembly boundaries

Keep assemblies aligned with domain modules where practical.

Do not create circular assembly references.

Desired dependency flow:

```text
Core ← Gameplay ← Presentation/UI
Core ← Activity ← Platform adapters
Core ← Persistence adapters
```

## 8. Coding style principles

- Prefer explicit types and small domain services.
- Use dependency injection/composition root rather than ubiquitous singletons.
- Keep MonoBehaviours focused on presentation/lifecycle glue.
- Keep core calculations in plain C# where they can be unit tested.
- Avoid static mutable global state.
- Avoid per-frame LINQ/allocations in hot paths.
- Use `async` carefully around Unity lifecycle and native callbacks.
- Log through a wrapper so sensitive production logs can be controlled.

## 9. IDs

Persistent IDs are API.

Never:
- persist Unity instance IDs.
- rename shipped region/building/project IDs without migration.
- derive persistent ID from display name.

Use stable strings/GUIDs.

## 10. Save schema changes

Any breaking save change requires:

1. Increment `schemaVersion`.
2. Add migration.
3. Add migration test.
4. Update `DATA_MODEL.md`.

Never wipe saves just because a field changed.

## 11. Activity-provider rules

All platform providers implement the shared abstraction.

Never directly call:
- `CMPedometer`
- Android `SensorManager`
- HealthKit
- Health Connect
- location APIs

from reward UI or restoration systems.

Correct layering:

```text
Platform API
→ IActivityProvider
→ Activity normalization
→ trust/reconciliation
→ reward calculator
→ VitalityLedger
```

## 12. Reward transaction safety

A crash must not produce duplicate movement currency.

When processing new activity:

```text
read provider delta
→ calculate accepted reward
→ prepare updated provider cursor
→ commit reward + cursor together
```

If true filesystem/database transactions are unavailable, emulate transactional behavior through atomic save replacement and idempotent dedup keys.

## 13. Building placement rules

Placement mutations must go through `BuildingPlacementService`.

Do not directly change a transform and assume that is saved state.

Correct flow:

```text
preview transform
→ validate
→ commit placement data
→ presentation applies committed state
```

## 14. Builder/Explore synchronization test

Any change touching building state must test:

1. Restore building.
2. Move building.
3. Save.
4. Reload.
5. Enter Explore mode.
6. Confirm exact same placement.

This is a mandatory regression test category.

## 15. Native-code rules

### iOS
- Keep bridge narrow.
- Guard device-only calls with platform compilation conditions.
- Do not implement gameplay economy in Swift/Objective-C.
- Permission prompts need correct purpose strings.

### Android
- Keep Kotlin/Java plugin narrow.
- Return sensor readings and permission state.
- Handle API-version checks explicitly.
- Do not introduce permanent background services without a documented product need.

## 16. Location rules

Any new location access must answer:

1. Why is location necessary?
2. Can this feature work without precise location?
3. Is access limited to an explicit active session?
4. What is retained after the session?
5. What happens when permission is denied?

Normal passive Vitality earning must continue without location.

## 17. Health platform rules

HealthKit and Health Connect are **not** default MVP dependencies.

If an issue proposes enabling either:
- Re-read current official policy/docs.
- Update `RESEARCH_NOTES.md`.
- Update privacy disclosures.
- Document requested data types.
- Define canonical source/dedup logic.
- Ensure manual/imported data trust policy is explicit.

## 18. Performance rules

Do not optimize by speculation alone.

Profile on physical device.

Always inspect both:
- Builder camera, which can see most of the region.
- Explore camera, which can create close-up shader/geometry load.

When adding content, watch:
- draw calls.
- overdraw.
- texture memory.
- shader complexity.
- GC allocations.
- NPC update cost.

## 19. Testing priorities

Highest-value deterministic tests:

- Vitality credit/spend invariants.
- Duplicate activity intervals.
- Android cumulative counter reset.
- Offline production cap.
- Clock moved backward.
- Restoration prerequisites.
- Building footprint validity.
- Save migration.
- Region unlock criteria.

## 20. Debug tools are first-class

Create developer tools rather than manually hacking saves.

Expected debug actions:
- Add steps.
- Simulate walk/run.
- Set Vitality.
- Complete project.
- Reset region.
- Unlock region.
- Advance clock.
- Simulate Android reboot.
- Simulate suspicious vehicle movement.
- Toggle sensor availability.

Debug systems must be excluded or protected in release builds.

## 21. Documentation expectations

Each new major system should have:
- purpose.
- ownership.
- public interface.
- persistent data impact.
- failure behavior.
- test strategy.

If code comments merely repeat syntax, omit them. Use comments to explain constraints, edge cases, and non-obvious reasons.

## 22. Decision records

If changing a major choice such as engine, persistence model, activity source, region loading strategy, or builder placement strategy, add an ADR under:

```text
docs/adr/NNNN-short-title.md
```

ADR template:

```markdown
# ADR NNNN — Title

## Status
Proposed | Accepted | Superseded

## Context
...

## Decision
...

## Consequences
...
```

## 23. Definition of done for a feature

A feature is not done because it works once in Editor.

Done means:
- Implementation complete.
- Relevant tests pass.
- Save/load impact tested.
- Mobile lifecycle considered if applicable.
- Permission denial handled if applicable.
- No architecture invariant broken.
- Documentation updated if behavior changed.
- Tested on physical device for native/performance-sensitive work.

## 24. Agent stop conditions

Stop and flag the conflict instead of improvising if:
- A task requires destructive save migration.
- A task requires precise location for ordinary passive steps.
- A task creates an unbounded speed reward.
- Builder and Explore mode would need separate authoritative state.
- A platform permission is being added without user-facing need.
- A new feature materially expands scope beyond the current roadmap milestone.

## 25. Immediate next work

Until implementation begins, agents should follow `ROADMAP.md` from **Phase 0**.

The first code PR should bootstrap the Unity 6.3 LTS URP project and project structure; it should not attempt to implement the whole game in one pass.