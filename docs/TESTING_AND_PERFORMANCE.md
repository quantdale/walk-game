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

As of 2026-08-27 (M8.7 canonical-state & certification-integrity closure, ADR 0007 amendment; M8.6 harness preserved), `dotnet test verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj`
passes **224/224** (213 baseline + 11 new M8.7 canonical-state regressions in `M87SaveIntegrityClosureTests`) and `scripts/verify-unity-static.ps1` passes the pinned Unity version,
asset metadata, package invariants, and Bootstrap scene checks (108 assets/108 metas — added `M87SaveIntegrityClosureTests.cs` + meta).
`scripts/verify-release-hygiene.ps1` adds a CI-runnable privacy/release audit (no GPS/save-path
logging, Log-wrapper enforcement, minimal manifest). Player-facing state is covered by
`PlayerExperienceTests`, exactly-once by `ActivityServiceTests` + `InterruptedSessionRecoveryTests`
(process-death, late deliveries, save/reload), pacing by `AshfallEconomyPacingTests`, and the
**real application transaction protocol** by `MovementDeliveryDurabilityTests`,
`ApplicationOrchestrationTests`, and the M8.5 suites:

- `OperationOwnershipTests` — timeout/completion races have exactly one terminal owner;
  abandoned preparations/stops converge provider state without stranding claims or losing retryability.
- `ProviderLifetimeTests` — idempotent `Shutdown()` contract: refuses new work, restores staged
  claims instead of consuming, never fabricates durable acknowledgment.
- `RuntimeOwnershipOrchestrationTests` — debug/vehicle completions through the shared coordinator on
  failed persistence (marker repair), hung-stop convergence, durability-gated reward presentation.
- `DedupCanonicalizationTests` + the dirty-target rollback fidelity test in
  `SaveIntegrityApplicationTests`.

The coordinator, `GameHost.CommitChangesWithOutcome()`, `OperationLease`/`ProviderOperations`,
and `ExpeditionResultPresentation` are the extracted, headlessly certified surface; ticker and
Expedition frame timing, scene composition, and provider JNI / CoreMotion callbacks remain
UNVERIFIED without an editor/device.

### M8.6 certification-harness hardening (AUTOMATED)

M8.6 executed the in-repo, editor/device-independent certification work and left every
EDITOR/DEVICE/iOS lane `UNVERIFIED` by a precise environment blocker (no licensed Unity editor,
no Android Build Support, no physical device, no macOS). The harness itself was made fail-closed:

- `scripts/verify-unity-editmode.ps1` and `scripts/verify-unity-playmode.ps1` now require a
  non-empty, parseable NUnit result XML with **zero failures** (shared `Test-NUnitResultXml` in
  `scripts/cert-script-helpers.ps1`). A Unity exit 0 with a missing/invalid/incomplete result
  file now fails the gate instead of overstating success.
- `scripts/verify-android-smoke.ps1` now binds **every** `adb` command to one exact serial via a
  new `-DeviceSerial` option; with no serial it fails closed unless exactly one authorized/online
  target is present, and records manufacturer/model/release/SDK/ABI, step-counter availability,
  APK SHA-256 and source SHA. Emulator/no-step-counter runs are labeled `lifecycle-only`.
- A 288-file deep re-audit added mandatory evidence-integrity findings (R1-R9 / E1-E10). This
  session closed the script-level ones engine-free: a fail-closed **R4** Unity toolchain-identity
  preflight (`Get-UnityPinnedVersion` / `Test-UnityEditorMatchesPin`) wired into both test wrappers;
  **R6** idempotent clean-install uninstall (`Uninstall-AndroidPackageIdempotent`); **R7** `finally`
  summary/logcat persistence with `finalDisposition`; and **R17.2.10** foreground/resumed launch
  evidence (`Get-AndroidForegroundActivity`). `R5` serial-binding of every direct `adb` call was audited
  and confirmed.
- New `scripts/Test-CertificationScripts.ps1` (engine-free, no Unity/adb/device) locks these
  semantics with **35/35** regression checks (up from 16/16) and is part of the local gate set.

### M8.7 canonical-state & guard-integrity closure (AUTOMATED)

M8.7 closed the parseable-save structural-integrity family and the first-push lost-update deadlock engine-free:

- `SaveValidator` H1/H2/H3: null `RegionState` reconstruction/prune, `regionId` normalized to dictionary key, null `VitalityTransaction` prune (with `SaveValidationReport` counters); `WorldState.GetOrCreateRegionState` self-heals null values; `ProfileStateCopier` skips null history elements as a rollback-boundary defense. 11 focused regressions in `M87SaveIntegrityClosureTests` (H1 current/unreachable null, H2 key mismatch + no-split-identity, H3 prune + copier tolerance + failed-commit rollback, S8 round-trip/idempotence, H4 full structural matrix, S7 no-minting) plus the existing dirty-target/dedup tests. No Vitality is minted and no progression is fabricated; re-repair is idempotent.
- H5 first-push guard: `pre-push`, `check-remote-advance.sh` and `Check-RemoteAdvance.ps1` now probe exact ref existence via `ls-remote` to distinguish absent branch (allow first push) from transport/auth failure (fail closed), while preserving ancestor/race, deletion, and no-force policy. `Test-AgentGuards.ps1` now covers first-push to absent branch, similar-name exact-ref, unreachable origin, and the existing contained/advanced/divergence cases (ps twin; sh twin environment-blocked in this sandbox, pre-existing, documented in IMPLEMENTATION_STATUS).

Unity compile/EditMode/PlayMode, Android IL2CPP/ARM64 build, physical step-counter exactly-once,
UX and performance/battery/thermal remain **UNVERIFIED** until a licensed editor and reference
hardware are available.

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
