# Walk Game — Development Roadmap

## 1. Roadmap philosophy

Build the **smallest complete proof of the product fantasy** before building breadth.

The highest-risk question is not whether Unity can render a city or whether a phone can count steps. It is:

> Does real-world movement feel meaningfully connected to restoring, arranging, and later exploring a region?

Therefore the roadmap prioritizes one complete region with fake activity data first, then native sensors, then polish, then additional content.

## 2. Phase overview

```text
Phase 0  Foundation / repo bootstrap
Phase 1  Gray-box region + core state
Phase 2  Restoration + builder loop
Phase 3  Explore mode
Phase 4  Activity integration
Phase 5  Idle/incremental economy
Phase 6  Vertical-slice content + art
Phase 7  Performance, privacy, device hardening
Phase 8  Closed playtest
Phase 9  Post-MVP expansion
```

## Campaign status — 2026-08-23

The Ashfall Basin campaign has moved the one-region slice beyond domain-only gray-box
coverage: the repository now contains a reusable procedural basin environment, canonical
stage-driven restoration presentation, responsive Builder/Explore HUD and project UX,
producer/offline summaries, contextual onboarding, touch controls, NPC/lore interactions,
and player-facing Expedition lifecycle feedback. The deterministic suite is 113/113
passing and the static Unity audit passes. Unity editor, PlayMode, Android build, and
device gates remain **UNVERIFIED** because this environment has no Unity entitlement and
the installed editor lacks Android Build Support. Region 2 and all multiplayer/combat/
backend expansion remain out of scope.

## 3. Phase 0 — Foundation

### Goal
Create a production-shaped Unity project before gameplay code spreads.

### Tasks
- [ ] Install/pin Unity 6.3 LTS.
- [ ] Create Unity project using URP.
- [ ] Configure Android and iOS build profiles.
- [ ] Add `.gitignore` for Unity.
- [ ] Add assembly definitions for major modules.
- [ ] Add Input System.
- [ ] Add test assemblies.
- [ ] Create bootstrap scene.
- [ ] Create persistent service composition root.
- [ ] Implement `ClockService`.
- [ ] Implement save serializer abstraction.
- [ ] Implement first local save repository.
- [ ] Add basic logging wrapper.
- [ ] Create debug/dev menu shell.

### Acceptance
- Android development build launches on physical device.
- iOS Xcode project can be generated.
- Empty profile can save/reload.
- Automated EditMode test runs successfully.

## 4. Phase 1 — Gray-box region and canonical state

### Goal
Prove that a contained region can be represented entirely from canonical data.

### Tasks
- [ ] Implement `RegionDefinition`.
- [ ] Implement `RegionState`.
- [ ] Implement stable building IDs.
- [ ] Create Ashfall Basin gray-box terrain.
- [ ] Author 6–10 building ruins.
- [ ] Implement `BuildingActor`.
- [ ] Instantiate buildings from state.
- [ ] Implement region load/unload.
- [ ] Implement save migration version 1.
- [ ] Add test profile fixtures.

### Acceptance
- Region loads from definition/state.
- Restart reconstructs exact same ruin state.
- No mutable player progress is stored only in scene objects.

## 5. Phase 2 — Restoration and builder loop

### Goal
Create the first satisfying loop without real sensors.

### Tasks
- [ ] Implement `VitalityLedger`.
- [ ] Add Debug Activity Provider.
- [ ] Debug action: +1,000 steps.
- [ ] Convert accepted debug steps to Vitality.
- [ ] Implement restoration project definitions.
- [ ] Implement prerequisite validation.
- [ ] Implement project transaction flow.
- [ ] Implement ruin → restored visual swap.
- [ ] Implement builder camera.
- [ ] Implement touch selection.
- [ ] Implement placement grid/mask.
- [ ] Implement building move preview.
- [ ] Implement collision/footprint validation.
- [ ] Persist placement.

### Acceptance
A tester can:
1. Press debug +steps.
2. Gain Vitality once.
3. Restore a ruined building.
4. Move it.
5. Close app.
6. Reopen and see it in the same place.

This is the first real gameplay checkpoint.

## 6. Phase 3 — Third-person Explore mode

### Goal
Deliver the emotional payoff of entering the rebuilt region.

### Tasks
- [ ] Add third-person controller.
- [ ] Add Explore camera.
- [ ] Implement Builder ↔ Explore mode state machine.
- [ ] Add safe player spawn.
- [ ] Ensure moved buildings are identical in Explore View.
- [ ] Implement authored path/nav strategy.
- [ ] Add one NPC.
- [ ] Add one lore interaction.
- [ ] Add basic region ambience.
- [ ] Add return-to-builder action.

### Acceptance
- Move a building in Builder.
- Enter Explore.
- Walk to that exact building.
- Exit Explore.
- No state divergence.
- Player cannot leave the region boundary.

## 7. Phase 4 — Native activity integration

### Goal
Replace fake steps with real phone movement without changing game-domain code.

### 4A — Android
- [ ] Create Android native sensor bridge.
- [ ] Request `ACTIVITY_RECOGNITION` contextually.
- [ ] Read `TYPE_STEP_COUNTER`.
- [ ] Handle first baseline.
- [ ] Handle reboot/reset.
- [ ] Reconcile foreground/resume deltas.
- [ ] Add provider tests with simulated raw counters.

### 4B — iOS
- [ ] Create Core Motion native bridge.
- [ ] Add `NSMotionUsageDescription`.
- [ ] Query `CMPedometer` history.
- [ ] Respect seven-day history limit.
- [ ] Track `lastSuccessfulSyncUtc`.
- [ ] Implement live updates for Expeditions.

### 4C — Active Expeditions
- [ ] Add session start/stop UX.
- [ ] Add moving duration.
- [ ] Add distance where available.
- [ ] Add optional location permission flow.
- [ ] Add plausibility/trust calculation.
- [ ] Add capped distance/endurance bonuses.
- [ ] Add vehicle-like test session to debug provider.

### Acceptance
- Real-world walk increases Vitality after reconciliation.
- Restart does not double-credit.
- Android reboot does not produce negative/huge reward.
- iOS historical sync does not re-credit intervals.
- Base steps work without GPS permission.
- Vehicle-like fake test loses optional bonus.

## 8. Phase 5 — Idle/incremental systems

### Goal
Make restored structures continue generating value while walking remains the primary gating force.

### Tasks
- [ ] Implement `ProductionService`.
- [ ] Implement producer definitions.
- [ ] Add offline-cap calculation.
- [ ] Add 3–5 producing buildings.
- [ ] Add collection UI.
- [ ] Add upgrade tiers.
- [ ] Add resource chains.
- [ ] Add time-change anomaly handling.

### Acceptance
- Close app for controlled simulated time.
- Reopen.
- Production equals deterministic expected result.
- Moving device clock backward creates no negative production.
- Offline production cannot replace Vitality project gates.

## 9. Phase 6 — Vertical-slice content

### Goal
Turn systems into one emotionally convincing region.

### Ashfall Basin content target
- [ ] 10–15 restoration projects.
- [ ] 6–10 building ruins.
- [ ] Dry river restoration.
- [ ] Dead grove restoration.
- [ ] Workshop.
- [ ] Greenhouse.
- [ ] Water station.
- [ ] Research structure.
- [ ] 3 NPCs.
- [ ] 5 lore discoveries.
- [ ] 1 multi-stage transit landmark.
- [ ] 4–6 region visual stages.
- [ ] Dead → alive lighting/audio transition.
- [ ] 10+ simple decorations.
- [ ] First-region completion sequence.

### Acceptance
An external tester can play from dead world to the region's first major flourishing milestone and understand the loop without developer explanation.

## 10. Phase 7 — Performance, privacy, and hardening

### Goal
Turn prototype behavior into mobile-production behavior.

### Performance tasks
- [ ] Select low/mid/high reference devices.
- [ ] Profile Builder View.
- [ ] Profile Explore View.
- [ ] Add LODs.
- [ ] Optimize vegetation.
- [ ] Reduce real-time lights.
- [ ] Inspect overdraw.
- [ ] Audit texture sizes/compression.
- [ ] Audit GC allocations.
- [ ] Test thermal/battery behavior during active Expedition.

### Save/data tasks
- [ ] Atomic writes.
- [ ] Backup recovery.
- [ ] Schema migration tests.
- [ ] Corruption test.
- [ ] Time-zone test.

### Privacy tasks
- [ ] Finalize permission strings.
- [ ] Confirm location is optional.
- [ ] Ensure no raw GPS debug logs in release.
- [ ] Draft privacy policy requirements.
- [ ] Confirm analytics schema excludes raw sensitive traces.

### Acceptance
- Stable target frame rate on selected mid-range device.
- No duplicate-step bugs in lifecycle tests.
- Permission denial produces understandable fallback.
- Save corruption recovery verified.

## 11. Phase 8 — Closed playtest

### Goal
Validate motivation, not just correctness.

### Cohort
Start with a small internal/external group across:
- Casual walkers.
- Moderate walkers.
- Runners.
- Different Android hardware.
- At least two iPhone generations.

### Questions
Measure:
- Did users understand why walking mattered?
- Did they open the game because they were close to a project?
- Did builder customization feel worthwhile?
- Did Explore mode make progress feel more meaningful?
- Was idle output satisfying or confusing?
- Did runners understand bonuses without feeling required to chase speed?
- Did casual walkers progress too slowly?

### Quantitative signals
- Daily accepted step bucket.
- Vitality earn/spend ratio.
- Time to first building restore.
- Time to first region-stage transition.
- Builder movement usage.
- Explore entry rate.
- Idle cap hit rate.
- Expedition adoption.

### Exit criteria
Do not build Region 2 at full production quality until:
- Core loop retention feedback is positive.
- Economy pacing is not obviously broken.
- Explore mode is used by a meaningful share of testers.

## 12. Phase 9 — Post-MVP expansion

Prioritize based on playtest evidence.

Candidate sequence:

### 9A — Second region
- Different biome and restoration chain.
- Reuse systems, prove content scalability.

### 9B — World map and region travel polish
- Transit fiction.
- Region completion display.
- Cross-region resource dependencies only if not burdensome.

### 9C — Advanced era
- Flourishing → beyond-restoration projects.
- Advanced architecture.
- New long-term Knowledge economy.

### 9D — Wearable imports
- HealthKit.
- Health Connect.
- Dedicated policy/privacy review.
- Canonical source/dedup architecture.

### 9E — Cloud save
- Account optional if possible.
- Cross-device state.
- Conflict resolution.

### 9F — Social systems
Only after core game works solo.

Possible:
- Visit snapshots of friends' regions.
- Cooperative restoration goal.
- Cosmetic/community milestones.

Avoid competitive speed leaderboards by default.

## 13. Milestone naming

Recommended production milestones:

### M0 — Project boots
Unity project builds to Android.

### M1 — The Ruin
Gray-box region loads from state.

### M2 — First Restoration
Fake steps → Vitality → restored building.

### M3 — My Town
Restored buildings can be moved and saved.

### M4 — Walk Inside
Third-person mode shows the same town.

### M5 — Real Steps
Phone movement drives the game on iOS/Android.

### M6 — Living While Away
Idle production works.

### M7 — Ashfall Vertical Slice
One polished region complete.

### M8 — Device Ready
Performance/privacy/lifecycle hardened.

### M9 — Playtest Validated
Evidence supports expanding content.

## 14. Task priority rule

When choosing between two tasks, prefer the one that reduces risk in this order:

1. State integrity.
2. Movement reward correctness.
3. Builder/Explore synchronization.
4. Player-visible restoration feedback.
5. Mobile performance.
6. Content breadth.
7. Meta/social features.

## 15. What not to do early

Do not spend early months on:
- Huge world map.
- 20 regions.
- Multiplayer backend.
- Detailed traffic AI.
- Sophisticated citizen simulation.
- Combat system.
- Seasonal live ops.
- Procedural cities.
- Cosmetic store.

A beautiful wrong game is still wrong. Prove the movement-restoration fantasy first.

## 16. Suggested issue breakdown for coding agents

Each implementation PR should usually stay within one vertical concern.

Examples:
- `feat/save-core`
- `feat/region-state`
- `feat/vitality-ledger`
- `feat/restoration-projects`
- `feat/builder-placement`
- `feat/explore-mode`
- `feat/activity-debug-provider`
- `feat/android-step-provider`
- `feat/ios-pedometer-provider`
- `feat/offline-production`

Avoid mega-PRs that simultaneously modify activity, world state, UI, and persistence unless the task is specifically a small vertical slice.
