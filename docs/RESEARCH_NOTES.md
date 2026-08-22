# Walk Game — Implementation Research Notes

> Research snapshot: August 2026. Verify official documentation again when implementing platform-sensitive features or preparing store submission.

## 1. Executive findings

### Engine

**Recommendation: Unity 6.3 LTS.**

Unity announced Unity 6.3 LTS in December 2025 and explicitly recommends it for new productions and projects locking a production version. It has two years of standard LTS support.

Why it fits this game:
- Strong Android/iOS deployment workflow.
- Mature 3D mobile rendering via URP.
- Native plug-in support for platform motion APIs.
- Addressables can load region scenes asynchronously.
- One engine can support builder camera, touch UI, and third-person exploration.

Primary source:
- https://unity.com/blog/unity-6-3-lts-is-now-available

## 2. Unity mobile delivery

Unity's mobile guidance emphasizes:
- URP optimization.
- profiling on physical devices.
- Addressables for reducing initial content footprint/load management.
- Input System for touch.

Unity Android builds can produce Android App Bundles for Google Play. iOS builds generate an Xcode project that is compiled/signatured with Xcode on macOS or through compatible cloud build tooling.

Sources:
- https://learn.unity.com/collection/ship-your-first-mobile-game
- https://docs.unity3d.com/6000.0/Documentation/Manual/system-requirements.html

### Design implication

Do not build all regions into one always-loaded scene. Contained region scenes are a natural fit for asynchronous content loading.

## 3. Unity Addressables

Addressables supports asynchronous scene loading through `Addressables.LoadSceneAsync`, including loading without immediate activation. This is useful for:
- world-map transitions.
- loading one contained region at a time.
- future remote content delivery.

Reference:
- https://docs.unity3d.com/Packages/com.unity.addressables@1.21/api/UnityEngine.AddressableAssets.Addressables.LoadSceneAsync.html

### Design implication

Use Addressables when the project moves beyond the first region. Do not introduce remote catalog complexity before needed.

## 4. Unity native plug-ins

Unity supports calling native platform code from C# through native plug-ins. iOS native bridges can expose Objective-C/C-compatible functions to C# and should be conditionally called on device.

Reference:
- https://docs.unity3d.com/Manual/NativePlugins.html

### Design implication

Create a narrow native sensor bridge and keep reward logic in C#. Platform code should return facts, not game currency decisions.

## 5. Apple Core Motion `CMPedometer`

Apple describes `CMPedometer` as an API for system-generated walking data. It supports:
- step count.
- walking/running estimated distance.
- historical queries.
- live pedometer updates.

`CMPedometerData` can expose:
- `numberOfSteps`.
- `distance`.
- `averageActivePace`.
- `currentPace` during live updates when supported.
- `currentCadence` during live updates when supported.

Core Motion requires `NSMotionUsageDescription`.

Sources:
- https://developer.apple.com/documentation/coremotion/cmpedometer
- https://developer.apple.com/documentation/coremotion/cmpedometerdata

### Critical limitation

Apple states historical `CMPedometer` queries expose only the past **seven days** of stored data.

Source:
- https://developer.apple.com/documentation/coremotion/cmpedometer/querypedometerdata(from:to:withhandler:)

### Design implication

Core Motion is excellent for MVP phone-based counting, but users who leave the app unopened for longer than available history can lose unimported historical opportunity. Later HealthKit support can address wearable/history scenarios if policy requirements are met.

## 6. Android step sensors

Android provides motion sensors including:
- `TYPE_STEP_COUNTER`.
- `TYPE_STEP_DETECTOR`.

`TYPE_STEP_COUNTER` returns the number of steps taken since the last device reboot while the sensor was activated. Android documentation notes it has more latency (up to around 10 seconds) but greater aggregate accuracy than the per-step detector.

On Android 10/API 29+, step counter/detector use requires `ACTIVITY_RECOGNITION` permission.

Source:
- https://developer.android.com/develop/sensors-and-location/sensors/sensors_motion

### Design implication

Persist a baseline and credit **deltas**, not raw cumulative counter values. Treat a counter decrease as reboot/reset, establish a new baseline, and never create negative rewards.

## 7. Android battery strategy

Android's sensor documentation recommends retrieving the step counter at an interval appropriate to the app and keeping polling as infrequent as possible when real-time data is not required.

### Design implication

For normal gameplay, reconcile cumulative steps when app resumes instead of keeping a permanent service alive just to count every event.

## 8. Android Fused Location Provider

`FusedLocationProviderClient` is the primary Google Play services location entry point.

Important behavior:
- coarse or fine location permission is required.
- approximate/coarse location is intentionally obfuscated/throttled.
- continuous updates use `requestLocationUpdates`.
- background location requires additional platform handling/permission or foreground-service patterns.
- documentation warns repeated/continuous location can consume substantial power if not managed correctly.

Source:
- https://developers.google.com/android/reference/com/google/android/gms/location/FusedLocationProviderClient

### Design implication

Use location only for explicitly started active Expeditions. Passive steps must not require GPS.

## 9. Apple HealthKit step/workout capabilities

HealthKit provides step counts and walking/running distance, and can store/query workouts. Apple documents:
- `stepCount`.
- `distanceWalkingRunning`.
- running speed/stride/power and related metrics where available.
- workout routes containing Core Location samples.

Sources:
- https://developer.apple.com/documentation/healthkit/hkquantitytypeidentifier/stepcount
- https://developer.apple.com/documentation/healthkit/hkquantitytypeidentifier/distancewalkingrunning
- https://developer.apple.com/documentation/healthkit/creating-a-workout-route

### Source/provenance

HealthKit objects expose `sourceRevision` and device/source information describing the app/device that created a sample.

Source:
- https://developer.apple.com/documentation/healthkit/hkobject/sourcerevision

### Manual entry

Apple exposes `HKMetadataKeyWasUserEntered`, indicating whether a sample was entered by the user.

Source:
- https://developer.apple.com/documentation/healthkit/hkmetadatakeywasuserentered

### Design implication

If HealthKit is added, manual entries should not receive verified performance bonuses. Source metadata can contribute to trust analysis.

## 10. HealthKit authorization/privacy

HealthKit requires fine-grained permission by data type. Apple notes that apps should request access when needed rather than necessarily requesting every type at once.

Source:
- https://developer.apple.com/documentation/healthkit/authorizing-access-to-health-data

Apple also states:
- HealthKit should only be accessed for health/fitness purposes that are clear in marketing/UI.
- HealthKit data must not be used for advertising/marketing data mining.
- health data must not be sold to data brokers/advertising platforms.
- a privacy policy is required for apps using HealthKit.

Source:
- https://developer.apple.com/documentation/healthkit/protecting-user-privacy
- https://developer.apple.com/app-store/review/guidelines/

### Product risk

This game can plausibly qualify as a fitness/activity product because real-world movement is core functionality, but that positioning must be explicit if HealthKit is used. Do not treat HealthKit as a generic game-data API.

## 11. Health Connect platform status

As of current Android documentation:
- Android 14/API 34 includes Health Connect as a framework module.
- Android 13 and lower may use the installable Health Connect app.
- Health Connect supports Android 8 SDK-wise, with the app availability constraints documented by Google.

Source:
- https://developer.android.com/health-and-fitness/health-connect/get-started

## 12. Health Connect steps

Health Connect supports `StepsRecord` and recommends **aggregate queries** for cumulative step totals to avoid double counting from multiple sources and to improve accuracy.

Source:
- https://developer.android.com/health-and-fitness/health-connect/read-data

### June 2026 attribution change

Android documentation states that starting with the June 2026 Health Connect update, on-device steps are attributed to a device-specific **Synthetic Package Name (SPN)** rather than the generic `android` package name. Apps should not hardcode the SPN.

Source:
- https://developer.android.com/health-and-fitness/health-connect/features/steps

### Design implication

If source-filtering steps in a future Health Connect provider, use current platform APIs rather than hardcoded package identifiers.

## 13. Health Connect metadata/provenance

Health Connect records include metadata such as:
- data origin.
- device.
- client record ID/version.
- recording method.

Current recording-method categories include:
- unknown.
- manual entry.
- automatically recorded.
- actively recorded.

Source:
- https://developer.android.com/health-and-fitness/health-connect/metadata

### Design implication

Manual Health Connect entries should not qualify for verified running/performance bonuses. Automatically/actively recorded data can receive higher confidence depending on other evidence.

## 14. Health Connect workout data

Health Connect supports `ExerciseSessionRecord` and associated data types such as:
- distance.
- speed.
- step cadence.
- exercise routes.

Source:
- https://developer.android.com/health-and-fitness/health-connect/experiences/workouts

### Design implication

A later wearable import layer can recognize legitimate run/workout sessions without implementing all wearable hardware directly.

## 15. Google Play Health policy

Google Play requires developers to complete Health Apps declarations. Health Connect access is restricted to approved health/fitness/medical/research use cases and requires minimum-scope access, user consent, disclosure, and privacy-policy compliance.

Sources:
- https://support.google.com/googleplay/android-developer/answer/14738291
- https://support.google.com/googleplay/android-developer/answer/16558241

### Product risk

Health Connect should not be an MVP dependency. Add it only after the game has a clear Activity & Fitness product position and disclosure/privacy implementation.

## 16. Why not reward raw speed directly

This conclusion is primarily a product/safety/anti-cheat inference:

- GPS speed is easier to fake or generate using vehicles than step cadence.
- Mobile GPS is noisy.
- Infinite speed multipliers create unsafe exercise incentives.
- The game does not need elite-performance measurement to reward runners.

Therefore:
- steps are the universal currency source.
- distance and duration provide capped bonuses.
- pace/speed mostly classify/validate sessions.
- top speed is not a progression multiplier.

## 17. Why builder and Explore View should share data

This is an architectural inference from the game concept.

If each mode owns separate scene arrangements, every building move would require fragile synchronization. A canonical `RegionState` makes both modes deterministic projections of one state.

Therefore:
- save local building coordinates once.
- render the same data in both modes.
- preferably toggle cameras/controllers in the same loaded region during MVP.

## 18. Why not build seamless open world

The user's desired interaction explicitly confines third-person exploration to one region at a time and uses map/teleport-style travel between regions.

This constraint is beneficial technically:
- smaller memory footprint.
- bounded navigation.
- easier mobile performance.
- Addressable region loading.
- easier art production.

Treat contained regions as a permanent design feature.

## 19. Main technical risks

### Risk 1 — Duplicate step credit
Mitigation: provider cursors + atomic reward/cursor persistence.

### Risk 2 — Android counter reset
Mitigation: cumulative delta model with reboot/reset baseline.

### Risk 3 — iOS missed history
Mitigation: sync cursor + explain seven-day Core Motion limit; later HealthKit opt-in.

### Risk 4 — Dynamic building placement breaks NPC navigation
Mitigation: authored pedestrian corridors + constrained footprints in MVP.

### Risk 5 — Builder camera overdraw/performance
Mitigation: profile Builder View separately, aggressive LOD/vegetation optimization.

### Risk 6 — GPS battery drain
Mitigation: GPS only in explicit sessions; stop updates immediately.

### Risk 7 — Health-platform store rejection
Mitigation: keep MVP on phone sensors; policy-review HealthKit/Health Connect before enabling.

### Risk 8 — Scope explosion
Mitigation: one-region vertical slice, no traffic simulation, no seamless world, no multiplayer before validation.

## 20. Research checklist before implementation changes

Agents changing platform integrations must:

1. Check the current official Apple/Google documentation.
2. Record SDK/API version assumptions in the PR.
3. Update this research file if behavior materially changed.
4. Add tests for platform edge cases.
5. Avoid copying old Stack Overflow/blog patterns when official APIs have changed.

## 21. Research conclusion

The game is technically feasible on mobile if it stays disciplined:

- contained regions instead of seamless open world.
- one canonical region state.
- mobile-friendly Unity pipeline.
- low-power phone pedometer APIs for core movement.
- optional GPS only for explicitly started activity sessions.
- capped rewards for distance/duration rather than unlimited speed.
- health-platform integration deferred until policy/privacy work is ready.

The largest risks are product scope, activity reconciliation correctness, and mobile performance—not the fundamental ability to count steps or render a third-person region.