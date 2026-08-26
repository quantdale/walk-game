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

Historical queries are naturally retryable under the ADR 0009 delivery
contract: `PreparePassiveDeliveryAsync` wraps the queried window in a prepared
delivery without consuming anything provider-private, and both resolutions are
no-ops. If the application commit fails, the profile rollback rewinds the
durable successful-sync cursor so the identical window is re-queried next
cycle; durable dedup/cursor state suppresses anything that did commit.

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

Delivery lifecycle (ADR 0009): the folded delta is staged as a prepared claim
(`ClaimPending`) and delivered passively; the provider drops the claim only on
durable acknowledgment and restores it for one same-process retry when the
profile commit is rejected. The persisted `androidLastRawStepCounter` advances
only inside a committed profile, so process death before commit replays the
uncommitted window exactly once from the durable cursor plus the live absolute
counter. Completed Expedition base steps are held as a completion claim and
returned to the passive stream when their commit fails.

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

Implemented policy (campaign):

- Passive credit keys are `providerId:intervalStart:intervalEnd` in a bounded FIFO store.
- Completed Expeditions additionally record `session:<id>` in `creditedSessionIds`; any
  re-delivery of the same session result pays nothing again.
- While an Expedition is active it owns the movement window: providers emit no passive
  snapshots, and the domain suppresses any that arrive anyway. On credit, the sync cursor
  jumps past the session end so later passive windows cannot re-read those steps.
- Dedup stores serialize through the save pipeline (`entries` field + post-load rebuild);
  losing them would silently re-open already-paid windows after every restart.
- Interrupted sessions (M8): provider sessions never survive process death, but the
  domain suppression marker is persisted. A marker observed at composition is stale by
  definition; `ActivityService.RecoverInterruptedSession()` clears it at boot. Recovery
  credits nothing itself - movement made while the process was dead re-reads from the
  provider cursor through the normal passive stream - so a mid-Expedition kill costs
  the player neither lost steps nor double payment.
- Orchestration repair (M8.4 / ADR 0010): the same stale-marker recovery is now also
  applied in-process after a failed Expedition or passive `DurableMutation` commit that
  reverts the profile and resurrects a durable `activeSession` marker. The
  `ActivityTransactionCoordinator` rejects the provider delivery (`durable=false`) so
  base movement returns to the passive stream, then clears the resurrected marker without
  requiring a restart; the repair converges durably on the next successful commit and
  remains reconstructible via boot recovery if the process dies before then.
- Operation ownership (M8.5 / ADR 0011): late `PreparePassiveDeliveryAsync` completions are
  owned forever, not merely drained for a bounded window. When the ticker's 12-second
  scheduling deadline expires, terminal ownership transfers atomically to a cleanup owner
  that survives the coroutine; whatever completes later is rejected without processing
  (cursor untouched), so no provider claim can ever be stranded regardless of completion
  timing. Provider instances expose idempotent `Shutdown()` and GameHost releases native
  monitoring/live sessions before any service-graph rebuild or host destruction.

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

### Implemented contract (campaign)

- `IActivityProvider.RequestMotionPermissionAsync()` is the only prompt path; UI calls it
  exclusively from explicit user interaction (the HUD motion-access banner).
- Prompts fire only while the state is effectively NotDetermined; repeated calls are the
  sanctioned retry path (e.g. after enabling access from OS Settings, followed by a plain
  `RefreshAsync`).
- `MotionPermissionCoordinator` (engine-free) sequences requests, never stacks prompts,
  and treats denial as a normal outcome with consequence-focused copy
  (see section 16), not an error state.
- Android: Denied is distinguished from NotDetermined via the rationale hint plus a
  process-side completed-request flag; step-counter monitoring starts only after grant.
- iOS: while NotDetermined, one benign asynchronous Core Motion query triggers the
  system dialog; status is polled to resolution.

### Player-facing Expedition surface (current slice)

`ExpeditionController` owns the runtime start/poll/finish presentation, but not reward
math. It observes each provider task from a coroutine, claims the domain's active-session
window only after provider start succeeds, and sends the final facts through
`ActivityService.ProcessSessionResult`. The HUD exposes Walk/Run start buttons, live
steps/distance/moving-time, a safe finish action, bounded-bonus copy, and a concise
resulting Vitality summary. Permission, unavailable-sensor, and interrupted-session
states use consequence-focused player copy.

When the app loses focus or pauses, the active session is marked paused in the UI and
polling waits without blocking or inventing movement. On resume it reports that tracking
is live again; a stop failure abandons the in-memory claim so passive movement remains
safe. This lifecycle path is automated/domain-ready but native background/device behavior
is still **UNVERIFIED**.

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
