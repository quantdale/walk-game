# ADR 0006 — Player-facing Ashfall presentation over canonical state

## Status

Accepted

## Context

The Ashfall domain and content catalog already described a complete restoration journey,
but the runtime presentation was a minimal gray-box shell. The vertical slice needed a
recognizable region, visible restoration stages, project/producer comprehension, mobile
Builder/Explore interaction, onboarding, and feedback without creating a second source of
truth or requiring final art assets.

## Decision

- Build Ashfall's first environment from a reusable `RegionEnvironmentPresenter` kit of
  named routes, district landmarks, primitive meshes, shared materials, labels, particles,
  and property blocks. The kit is a replaceable presentation layer; stable content IDs,
  `RegionState`, and domain services remain unchanged.
- Store explicit environment switches such as river flow, wetland life, and grove revival
  in additive `RegionState.environmentFlags`. Restoration reward actions author these
  flags; Builder and Explore read the same projected state.
- Keep building transformations canonical in grid placement/lifecycle/upgrade fields.
  `BuildingActor` may move a transient preview and show valid/invalid footprint feedback,
  but only `BuildingPlacementService` commits a confirmed move.
- Put player-facing orchestration in thin App/UI adapters: `ExplorationService` owns lore
  discovery, `ExpeditionController` owns task/lifecycle presentation, and `FeedbackController`
  centralizes optional audio/haptics and reduced-motion settings. No adapter computes
  movement rewards or writes scene-only progression.
- Use additive safe defaults and `SaveValidator` repair for the new settings/flag fields;
  save schema version 1 remains compatible.

## Consequences

- A player can see the dead basin transform through multiple visual channels while the
  canonical state remains the only authority.
- Procedural geometry is intentionally stylized and is a static-readiness result, not a
  substitute for final art or device profiling.
- The UI has a larger runtime surface and must remain behind the Unity compile/PlayMode
  gate; the current environment cannot claim that editor evidence.
- Final prefabs, audio clips, and native mobile lifecycle behavior can be added behind the
  same contracts without rewriting progression.
