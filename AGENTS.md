# AGENTS.md

This repository is being developed from a documentation-first master plan. Before changing code, read the project documentation.

## Required reading order

1. `docs/MASTER_PLAN.md`
2. `docs/ROADMAP.md`
3. `docs/TECHNICAL_ARCHITECTURE.md`
4. `docs/DATA_MODEL.md`
5. The domain-specific document for the task
6. `docs/AGENT_EXECUTION_GUIDE.md`

For movement, permissions, location, analytics, or health-platform work also read:

- `docs/ACTIVITY_REWARD_SYSTEM.md`
- `docs/MOBILE_ACTIVITY_INTEGRATION.md`
- `docs/PRIVACY_SAFETY_ANTI_CHEAT.md`
- `docs/RESEARCH_NOTES.md`

For performance-sensitive work read:

- `docs/TESTING_AND_PERFORMANCE.md`

## Product invariants

Do not silently violate these:

- Walking/steps are the universal movement baseline.
- Real-world movement generates Vitality, which powers restoration.
- Running receives bounded optional bonuses; raw top speed is never an uncapped reward multiplier.
- Passive step earning must work without GPS.
- Rest never destroys progress.
- Regions are bounded; travel between regions is via map/transit, not seamless third-person traversal.
- Builder View and Explore View project the same canonical `RegionState`.
- Building transforms are persisted in state, never only in scene objects.
- Native iOS/Android code returns sensor facts; C# domain logic computes rewards.
- Core gameplay remains offline-capable.
- MVP is one polished region, not a broad unfinished world.

## Current milestone

Do not infer the milestone from the original roadmap checkbox state. Instead:

1. Consult `docs/IMPLEMENTATION_STATUS.md` - it is the authoritative, evidence-tiered
   record of what is implemented vs verified vs unverified.
2. Run the certification commands before starting new work:
   `dotnet test verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj`
   (and `scripts/verify-unity-editmode.ps1` when an editor is available).
3. Claim only evidence you actually produced; editor/device gates without the tool
   or hardware present stay marked UNVERIFIED.

## Architecture changes

Major changes require an ADR under `docs/adr/NNNN-short-title.md` and corresponding documentation updates.

## Definition of done

A feature is not done until relevant tests, save/load behavior, lifecycle/permission fallbacks, and documentation changes are included. Native or performance-sensitive work must be validated on physical mobile hardware.

Exactly-once rule for movement: one piece of real movement must never generate Vitality twice across passive polling, Expeditions, process restarts, or save reloads. Any change touching activity credit must extend the regression coverage in `ActivityServiceTests`, `AndroidCounterReconciliationTests`, and `SaveLoadTests`.

See `docs/AGENT_EXECUTION_GUIDE.md` for the full workflow.