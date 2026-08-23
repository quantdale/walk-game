# Walk Game — Testing & Performance Plan

## 1. Objective

The game combines persistent state, native sensors, 3D mobile rendering, offline progression, and two views of the same region. Most serious bugs will occur at boundaries between those systems.

Testing must therefore prioritize:

1. State integrity.
2. Activity reconciliation.
3. Builder/Explore synchronization.
4. Mobile lifecycle.
5. Performance on physical devices.

## 1A. Current campaign evidence

As of 2026-08-23 (M8 campaign), `dotnet test verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj`
passes **124/124** and `scripts/verify-unity-static.ps1` passes the pinned Unity version,
asset metadata, package invariants, and Bootstrap scene checks. `scripts/verify-release-hygiene.ps1`
adds a CI-runnable privacy/release audit (no GPS/save-path logging, Log-wrapper enforcement,
minimal manifest). The new player-facing state surfaces are covered by `PlayerExperienceTests`,
the exactly-once boundary by `ActivityServiceTests` + the M8 `InterruptedSessionRecoveryTests`
(process-death recovery, late deliveries, save/reload), and pacing coherence by
`AshfallEconomyPacingTests`. The existing activity, placement, save/recovery, production,
permission, and full Ashfall playthrough suites remain part of the same automated gate.

The procedural environment kit uses shared materials, property blocks for state tinting,
static geometry after construction, reused UI rows, and `Physics.OverlapSphereNonAlloc`
for Explore interaction scans. These are static mobile-readiness measures, not device
performance measurements. Unity compile/EditMode/PlayMode and FPS, allocation, thermal,
and battery measurements remain **UNVERIFIED** until a licensed editor and reference
hardware are available.

## 2. Test pyramid

### Unit/EditMode
Use for deterministic domain logic:
- Vitality ledger.
- reward formulas.
- restoration prerequisites.
- region unlock logic.
- placement grid math.
- production calculations.
- save migrations.
- activity dedup/reconciliation.

### PlayMode
Use for Unity integration:
- building presentation.
- builder camera selection/movement.
- region state rehydration.
- Builder ↔ Explore transitions.
- region loading/unloading.

`Assets/WalkGame/Tests/PlayMode/RuntimeCertificationTests.cs` is the focused vertical-slice
certification layer. It covers Bootstrap composition, Ashfall Basin hydration, the
Builder → save → reload → Explore canonical-transform scenario, debug activity through
Vitality/restoration, fake-clock production collection, and permission denial without
blocking mode transitions. It intentionally complements rather than duplicates the
engine-free domain suite.

### Native/device
Use for:
- permissions.
- pedometer/step sensors.
- background/resume.
- Android reboot/counter reset.
- location session lifecycle.
- memory/thermal/battery.

## 3. Required unit tests

### Vitality ledger
- credit increases balance exactly once.
- spend cannot produce negative balance.
- duplicate transaction ID is rejected/idempotent.
- resulting balance is consistent.

### Activity rewards
- base steps calculate correctly.
- distance bonus caps.
- endurance bonus caps.
- no unlimited top-speed multiplier exists.
- low-trust session loses optional bonus but preserves accepted base steps.

### Android step counter
- first raw sample establishes baseline.
- increasing raw counter produces correct delta.
- same raw value produces zero delta.
- raw counter decrease establishes new baseline.
- reboot never produces negative steps.

### iOS reconciliation
- previously credited interval not credited twice.
- only new interval is credited.
- missing optional cadence/distance does not break base steps.

### Offline production
- normal elapsed time produces expected amount.
- offline cap is enforced.
- negative elapsed time produces zero.
- upgrade multiplier applied correctly.

### Restoration
- unmet prerequisites block project.
- insufficient resources block project.
- successful project spends once and marks complete.
- completed project cannot charge again.

### Placement
- valid footprint accepted.
- overlap rejected.
- out-of-mask placement rejected.
- fixed landmark cannot move.
- rotation footprint is recalculated.

### Save migrations
- every historical fixture migrates to latest schema.
- migration is deterministic.
- unknown/removed IDs follow explicit policy.

## 4. Mandatory regression scenario: dual view

Run whenever building/region/save code changes:

1. Start test profile.
2. Restore greenhouse.
3. Move greenhouse to known grid cell.
4. Rotate greenhouse.
5. Save.
6. Reload region.
7. Assert state coordinates.
8. Enter Explore mode.
9. Assert rendered building position/rotation.
10. Exit Explore.
11. Travel away/back if multi-region code exists.
12. Assert unchanged.

## 5. Mobile lifecycle scenarios

Test on both platforms:
- cold launch.
- background for 30 seconds.
- background for several minutes.
- process killed while backgrounded.
- device restarted.
- permission removed in OS Settings.
- network unavailable.
- timezone changed.
- clock moved backward.
- clock moved forward.

The game should never duplicate activity credit because of lifecycle transitions.

## 6. Permission matrix

### Motion denied
Expected:
- game still launches.
- builder/explore works.
- new real-world steps do not credit.
- clear non-blocking explanation.

### Location denied
Expected:
- passive steps still credit.
- Expedition can fall back to steps/time where designed.
- no crash or repeated aggressive prompt.

### Location approximate only (Android)
Expected:
- session remains usable if exact route precision is not required.
- trust calculation handles lower accuracy.

## 7. Debug simulation matrix

`DebugActivityProvider` must support deterministic fixtures:

```text
casual_walk_1000_steps
walk_5km
run_5km
long_run_20km
vehicle_10km
teleport_gps
no_location
no_cadence
android_counter_reset
timezone_change
clock_backward
```

Use these in both automated tests and developer UI.

## 8. Performance target philosophy

Do not invent a universal draw-call/triangle number and assume success. Mobile GPU/CPU capability varies widely.

Choose reference devices and profile real frames.

### Initial experiential targets
- 30 FPS minimum on selected low/mid target hardware.
- 60 FPS on stronger devices where practical.
- smooth builder pan/zoom.
- no severe hitch entering Explore mode.
- region transition target under ~5 seconds after content is local on a mid-range device.

These are goals, not guarantees; update with measured baselines.

## 9. Reference-device strategy

Before optimization, select at least:
- one lower-end supported Android.
- one mid-range Android.
- one higher-end Android.
- one older supported iPhone.
- one current/mid iPhone.

Record:
- OS version.
- chipset/GPU.
- RAM.
- display resolution/refresh.
- measured frame time.

Do not optimize only on the developer's fastest device.

## 10. Builder View performance risks

Builder camera often sees most of the region.

Likely bottlenecks:
- vegetation overdraw.
- too many unique materials.
- shadows across the full scene.
- transparent effects.
- large UI overlays.
- many visible NPCs/props.

Mitigations:
- LODs.
- GPU instancing.
- baked lighting.
- shadow distance controls.
- vegetation density tiers.
- occlusion where useful.
- simpler builder-mode effects if necessary.

## 11. Explore View performance risks

Third person creates different pressure:
- close-up texture/shader quality.
- animation.
- NPC AI.
- camera collision.
- particles.
- local real-time lights.

Profile separately from Builder View.

## 12. CPU budgets

Watch systems that run every frame:
- building placement validation.
- NPC navigation/AI.
- UI layout rebuilds.
- event dispatch storms.
- continuous sensor polling.

Rules:
- placement validation only while moving/previewing.
- idle production is timestamp math, not continuous simulation.
- NPC thinking can update at lower frequency.
- platform sensors should use callbacks/system counters rather than aggressive polling.

## 13. Memory strategy

Contained regions should make memory predictable.

At region transition:
- unload outgoing region assets.
- release Addressable handles properly.
- avoid static references retaining destroyed region objects.

Use Memory Profiler once real art content arrives.

Watch:
- texture memory.
- duplicated materials.
- audio clips.
- animation clips.
- Addressable bundle duplication.

## 14. Content budgets

Establish measured budgets after the vertical slice art pass.

For every content review record:
- texture resolution/format.
- material count.
- LOD presence.
- triangle counts by LOD.
- light/shadow usage.
- collider complexity.

Large static decorative meshes should use simple colliders or no collider unless interaction requires it.

## 15. Battery and thermal testing

The active Expedition path is the highest battery-risk feature because it may combine:
- location.
- motion sensors.
- app background execution.

Test:
- 30-minute walk.
- 60-minute walk/run.
- screen on versus locked/background where supported.

Measure qualitatively/with platform tooling:
- battery consumption.
- thermal state.
- CPU wakeups.
- location update frequency.

If active session battery use is excessive, reduce location sampling before compromising passive step tracking.

## 16. Network testing

MVP core loop should work offline.

Test:
- airplane mode at launch.
- network loss during region gameplay.
- network regain.

Future cloud features must queue or fail gracefully without corrupting local state.

## 17. Save fault injection

Test:
- truncated save.
- invalid JSON/binary.
- missing field in old schema.
- unknown project ID.
- disk write interrupted if testable.

Expected:
- load backup if current save is corrupt.
- never silently reset unless no recoverable state exists and user is informed.

## 18. Activity edge cases

Test:
- zero steps.
- huge but plausible hiking day.
- impossible huge delta.
- duplicate provider response.
- out-of-order timestamps.
- device clock in future.
- GPS route with poor accuracy.
- route with teleport jump.
- high distance but near-zero steps.

## 19. QA acceptance by milestone

### M2 First Restoration
- no duplicate fake-step credit.
- restoration transaction persists.

### M3 My Town
- placement round-trip exact.

### M4 Walk Inside
- Explore reflects builder placement.

### M5 Real Steps
- both native providers survive lifecycle/permission tests.

### M6 Living While Away
- offline calculations deterministic.

### M7 Vertical Slice
- no blocker bugs across complete first-region loop.

### M8 Device Ready
- target devices meet agreed performance/battery bar.

## 20. Release gates

Do not release a public build if:
- activity can be trivially double-credited by reopening app.
- save corruption regularly resets progression.
- normal passive steps require GPS.
- active Expedition continues location collection after stopping.
- Builder and Explore show divergent placement.
- UI encourages interaction while running.
- app fails on denied permissions.

## 21. Performance documentation rule

When an agent makes a meaningful performance change, PR should include:
- device tested.
- scene/mode.
- before measurement.
- after measurement.
- profiler evidence/metric used.

Avoid PR claims like "optimized performance" without measurement.
