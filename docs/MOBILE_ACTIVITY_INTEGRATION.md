# Walk Game — Mobile Activity Integration

## 1. Strategy

Use a two-stage platform strategy.

### Stage A — MVP phone-sensor path

**iOS**
- Core Motion `CMPedometer` for steps, estimated walking/running distance, and supported live pace/cadence.
- Optional Core Location only during explicitly started Expeditions.

**Android**
- `Sensor.TYPE_STEP_COUNTER` for cumulative steps.
- `Sensor.TYPE_STEP_DETECTOR` only where real-time step events are useful.
- `ACTIVITY_RECOGNITION` runtime permission on Android 10+.
- Optional Fused Location Provider only during explicitly started Expeditions.

This path avoids making HealthKit or Health Connect a prerequisite for the first playable build.

### Stage B — optional health-platform import

After privacy/policy review:
- HealthKit for Apple Watch/other Health sources.
- Health Connect for Android wearable/fitness-source data.

The health-platform layer should implement the same `IActivityProvider` contract.

## 2. Why phone sensors first

Advantages:
- Smaller permission surface.
- No account dependency.
- Core walking game works offline.
- Easier debugging.
- Lower policy risk than making health-platform access the only way the game functions.
- Stronger control over exactly which activity intervals have already been credited.

Tradeoff:
- Phone must generally be carried for steps to count.
- Wearable-only activity is not included until Stage B.

This tradeoff is acceptable for the vertical slice.

## 3. iOS Core Motion

### Required capability/permission

Use `CMPedometer` from Core Motion.

Add `NSMotionUsageDescription` to `Info.plist` with a clear user-facing explanation such as:

> Walk Game uses motion data to turn your walking and running into restoration progress in your game world.

Do not request permission before the user understands the feature.

### Capabilities

`CMPedometerData` can provide:
- `numberOfSteps`
- estimated `distance`
- `averageActivePace`
- live `currentPace` where supported
- live `currentCadence` where supported
- floor counts on supported devices

Not every metric exists on every device. Reward logic must tolerate null/unsupported metrics.

### Historical limit

Core Motion historical pedometer queries expose only the past **seven days** of available data.

Implication:
- Store `lastSuccessfulSyncUtc`.
- Query only unprocessed interval.
- If user returns after more than seven days, only available Core Motion history can be credited through this provider.
- A later HealthKit provider can extend history/wearable support if policy/product requirements are met.

### Query pattern

Conceptual Swift:

```swift
let pedometer = CMPedometer()
pedometer.queryPedometerData(from: start, to: end) { data, error in
    // Return only sensor facts to Unity.
}
```

### Live Expedition pattern

```swift
pedometer.startUpdates(from: sessionStart) { data, error in
    // Report current step count, distance, pace/cadence when available.
}
```

Stop updates when the session ends.

### Unity bridge

Preferred wrapper shape:

```csharp
public sealed class IosCoreMotionProvider : IActivityProvider
```

Native implementation can be Swift/Objective-C with an Objective-C/C-callable bridge as needed by Unity.

Keep the native API narrow:

```text
IsAvailable
GetAuthorizationStatus
Query(start, end)
StartSession(start)
StopSession
```

Do not put Vitality formulas in native code.

## 4. iOS active route / location

Precise location is optional.

If a player starts a verified Expedition and grants location:
- Use Core Location updates.
- Filter inaccurate samples.
- Calculate route distance on-device.
- Discard or down-weight teleport jumps and implausible intervals.

Normal daily step earning must not depend on location permission.

If route persistence is added through HealthKit later, Apple supports workout routes associated with HealthKit workouts, but that adds HealthKit authorization and privacy obligations.

## 5. Android step sensors

### `TYPE_STEP_COUNTER`

This sensor reports the number of steps taken since last reboot while the sensor was activated.

Characteristics:
- Higher latency than step detector, but generally more accurate for aggregate counts.
- Android documentation notes latency may be up to roughly 10 seconds.
- Suitable for cumulative daily progress.

### Permission

On Android 10/API 29 and higher, declare/request:

```text
android.permission.ACTIVITY_RECOGNITION
```

### Cumulative delta handling

Never credit the raw sensor value directly.

```text
first observation after install/reboot → establish baseline
later observation → max(0, currentRaw - previousRaw)
```

When raw counter decreases:
- Assume reboot/provider reset.
- Establish new baseline.
- Do not subtract previous credited steps.

Persist the latest raw counter after a successful reward transaction.

## 6. Android `TYPE_STEP_DETECTOR`

Step detector emits one event per detected step with lower latency.

Use only if needed for:
- Real-time Expedition UI.
- Immediate feedback.

Do not rely on step detector as the only long-term counter unless background lifecycle behavior is deliberately engineered and tested.

## 7. Android background behavior

Android process/background restrictions vary by version and OEM.

MVP principle:
- Use the system cumulative counter and reconcile when the app resumes rather than keeping a permanent background service solely to count every event.

This is simpler and more battery-friendly.

If later active Expeditions need reliable background location:
- Use an appropriately disclosed foreground service or platform-supported background mechanism.
- Stop it immediately when the session ends.

## 8. Android active location

Use Google Play services `FusedLocationProviderClient` where Google Play services are available.

For an active Expedition:
- request location updates at a sensible interval.
- prefer fine location only when verification needs it.
- respect Android 12+ approximate-location choice.
- stop updates on session completion.

Google documentation warns that continuous location requests can consume substantial power, especially when not stopped correctly.

### Mock locations

Fused Location APIs support mock location modes for testing, and Android location objects/providers may expose test/mock status depending on API path.

Do not rely on one mock-location flag as complete anti-cheat. Use it as one trust signal.

## 9. Health Connect — optional Stage B

### Current platform facts (researched August 2026)

- Android 14+ includes Health Connect as a framework module.
- Android 13 and lower may require the Health Connect app.
- Health Connect provides `StepsRecord`, `DistanceRecord`, `ExerciseSessionRecord`, route data, cadence/speed records, and metadata.
- For cumulative steps, Android recommends aggregation rather than blindly summing raw records because aggregation handles duplicates from multiple sources.
- Android Health Connect metadata identifies data origin/device and recording method, including manual, automatically recorded, and actively recorded.
- Starting with the June 2026 Health Connect update, on-device steps are attributed using a per-device synthetic package name rather than simply `android`.

### Game integration rules

If enabled:
- Request only the minimum required data types.
- Use aggregation for display/lifetime totals.
- For reward verification, inspect raw metadata/recording method when necessary so manual entries do not receive performance bonuses.
- Maintain stable record IDs/cursors for deduplication.
- Do not re-write imported Health Connect data back into Health Connect.

### Policy warning

Google Play restricts Health Connect access to approved health/fitness/medical/research use cases and requires health declarations/privacy disclosures. Treat Health Connect as a policy-reviewed feature, not a casual convenience library.

## 10. HealthKit — optional Stage B

HealthKit can provide:
- `stepCount`
- `distanceWalkingRunning`
- workout records
- running speed and other running metrics where available
- workout routes with user authorization
- source/device metadata
- metadata including whether a sample was user-entered

### Reward rules

If enabled:
- User-entered/manual samples should not receive verified performance bonuses.
- Source/device metadata may raise or lower confidence but should not be used to discriminate between legitimate device brands.
- Deduplicate records before credit.
- Remember that HealthKit can merge/coalesce data from multiple sources.

### Policy warning

Apple requires HealthKit to be used for health/fitness purposes that are clear in the app and marketing. HealthKit-derived data cannot be used for advertising/marketing data mining. The app must provide a privacy policy.

## 11. Provider selection

Possible provider priority:

### MVP

```text
Phone sensor provider → canonical step source
```

### Later

Option A:

```text
Health platform aggregate → canonical total
Phone sensor → live active-session validation
```

Option B:

```text
Phone sensor → default
Health platform → opt-in wearable import
```

Do not merge totals naively or steps will be double counted.

The team must choose a single canonical source strategy before shipping wearable support.

## 12. Deduplication

Each activity credit operation should have a deterministic dedup key.

Examples:
- provider record UUID(s).
- provider cursor + time interval.
- active session UUID.

Store credited keys in a bounded/compacted structure appropriate to provider capabilities.

## 13. Clock and timezone handling

Use UTC for provider synchronization.

Daily UI buckets use local date.

Handle:
- DST transitions.
- timezone travel.
- device clock moved backward.
- device clock moved forward.
- Android reboot.

Do not grant duplicate credit simply because a local day repeats during timezone change.

## 14. Sensor quality fallbacks

The reward engine must work with partial capabilities.

Examples:

### Steps only
- Base Vitality.
- No pace bonus.

### Steps + distance
- Base + bounded Explorer bonus.

### Steps + distance + cadence
- Base + Explorer + possible Rhythm.

### Steps + location session
- Full trust analysis.

Never deny all gameplay because one optional sensor is unavailable.

## 15. Permission UX

Ask in context.

### Motion permission
Ask when player first enables real-world movement rewards.

### Location permission
Ask only when player starts a verified Expedition that benefits from it.

### Health platform permission
Ask only when player enables wearable/history import.

Avoid requesting all permissions on first launch.

## 16. Native error model

Normalize platform errors into domain-level codes:

```text
PermissionDenied
PermissionNotDetermined
SensorUnavailable
NoData
TemporaryUnavailable
ProviderError
LocationUnavailable
```

UI should explain consequence, not platform internals.

Example:

> Motion access is off. You can still build and explore, but new real-world steps cannot generate Vitality until motion access is enabled.

## 17. Testing checklist — iOS

- Fresh permission grant.
- Permission denial.
- Permission changed in Settings.
- Walk with phone.
- Run with phone.
- App background/resume.
- Device reboot.
- Historical query within seven days.
- App unopened > seven days.
- Pace/cadence unavailable.
- Location denied during Expedition.
- Location granted during Expedition.

## 18. Testing checklist — Android

- Android 10+ activity permission.
- Step counter available.
- Step counter missing.
- Reboot raw-counter reset.
- App process killed and reopened.
- OEM battery optimization behavior.
- Location approximate-only.
- Fine location.
- Background Expedition lifecycle.
- Mock/test location signal.
- Health Connect unavailable/available if Stage B enabled.

## 19. Security boundary

Native providers are not trusted authorities merely because they are native.

Treat all client-reported activity as potentially tamperable.

For a single-player restoration game, this is acceptable: use plausibility checks to protect game balance.

If global leaderboards, prizes, or competitive rewards are added, server-side verification and a more rigorous threat model become mandatory.

## 20. Official references

Research these before implementing against current SDK versions:

- Apple Core Motion `CMPedometer`: https://developer.apple.com/documentation/coremotion/cmpedometer
- Apple `queryPedometerData`: https://developer.apple.com/documentation/coremotion/cmpedometer/querypedometerdata(from:to:withhandler:)
- Apple `CMPedometerData`: https://developer.apple.com/documentation/coremotion/cmpedometerdata
- Apple HealthKit: https://developer.apple.com/documentation/healthkit
- Apple HealthKit authorization: https://developer.apple.com/documentation/healthkit/authorizing-access-to-health-data
- Android motion sensors: https://developer.android.com/develop/sensors-and-location/sensors/sensors_motion
- Android Health Connect: https://developer.android.com/health-and-fitness/health-connect
- Android Health Connect read data: https://developer.android.com/health-and-fitness/health-connect/read-data
- Android Health Connect metadata: https://developer.android.com/health-and-fitness/health-connect/metadata
- Fused Location Provider: https://developers.google.com/android/reference/com/google/android/gms/location/FusedLocationProviderClient

SDK behavior and store policy can change; verify current official documentation during implementation and before release.