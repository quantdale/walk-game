# Walk Game — Privacy, Safety & Anti-Cheat

## 1. Product principle

Movement is the game's input, but the app should collect the **minimum amount of movement data required to deliver gameplay**.

The target is not perfect fraud prevention. The target is:

> Make legitimate walking/running the simplest and most rewarding way to play without creating dangerous incentives or invasive tracking.

## 2. Data categories

### Low-sensitivity game state
- Region progression.
- Building placement.
- Resources.
- Project completion.
- Cosmetic choices.

### Movement/fitness-related data
- Step count.
- Walking/running distance.
- Session duration.
- Pace/cadence.
- Workout metadata.

### Location data
- GPS coordinates/routes during explicitly started Expeditions if permission is granted.

Location should not be collected for passive daily step earning.

## 3. Data minimization rules

Default behavior:
- Compute movement rewards on-device.
- Persist accepted step totals and derived reward outcomes.
- Do not persist raw GPS routes after reward calculation unless a user-facing route feature explicitly requires them.
- Do not upload raw activity samples by default.
- Do not request heart rate, weight, sleep, nutrition, or other unrelated health metrics.

If a future feature needs new health data, it requires a documented product reason and privacy review.

## 4. Permission sequencing

### Motion/activity
Request when the user activates movement rewards.

### Location
Request only when the user starts an Expedition that uses route/distance verification.

### HealthKit / Health Connect
Request only when the user chooses wearable/history import and the feature has passed store-policy review.

Never request every sensitive permission at first launch.

## 5. Apple HealthKit policy risk

Apple's current guidance requires HealthKit access to serve health/fitness purposes that are clear in the product and marketing. Apple also states that HealthKit-derived health/fitness data must not be used for advertising/marketing data mining, and requires a privacy policy.

Implication:
- If HealthKit is enabled, the game must clearly position movement/fitness as a core product function, not as an unrelated game gimmick.
- Do not send HealthKit-derived activity into advertising profiles.
- Do not sell/share HealthKit-derived data with data brokers.
- Request only needed HealthKit types.

Reference:
- https://developer.apple.com/documentation/healthkit/protecting-user-privacy
- https://developer.apple.com/app-store/review/guidelines/

## 6. Google Play / Health Connect policy risk

Google Play currently restricts Health Connect access to approved health, fitness, medical care, or health research core use cases and requires clear disclosures, a privacy policy, and Health Apps declarations.

Implication:
- Treat Health Connect as a post-MVP opt-in feature.
- Validate that the release positioning qualifies as Activity & Fitness before requesting Health Connect permissions.
- Request the minimum data types.
- Complete the Play Console Health Apps declaration accurately.

Reference:
- https://support.google.com/googleplay/android-developer/answer/16558241
- https://support.google.com/googleplay/android-developer/answer/14738291

## 7. Safety design rules

### Never incentivize maximum speed

No unbounded reward based on top speed or fastest pace.

### Never punish rest

No city decay, resource loss, or destructive streak reset because a player does not move.

### Cap high-intensity bonuses

Performance bonuses should be modest and capped per session/day.

### Screen-attention safety

During active movement:
- Do not require tapping at timed intervals.
- Do not spawn "collect now" prompts that require immediate interaction.
- Avoid gameplay requiring the player to watch the screen while running.

### Environment safety

Do not design mechanics that encourage:
- crossing roads quickly for rewards.
- trespassing.
- chasing randomized GPS spawn points.
- movement during unsafe weather conditions.

The game uses movement amount, not real-world geographic scavenger hunting.

## 8. Personal improvement versus competition

Prefer:
- consistency.
- personal baseline improvement.
- lifetime distance.
- restoration milestones.

Avoid making athletic performance a dominant social ranking.

If leaderboards are later introduced, rank game achievements or optional categories rather than a single "fastest player" ladder.

## 9. Threat model

### Threat A — Vehicle travel

Attacker travels in car/bus while app records distance.

Mitigations:
- Distance bonuses require step/cadence evidence.
- Reject pedestrian bonuses for implausible speed bands.
- Detect teleport-like jumps.
- Use moving-time + step consistency rather than GPS distance alone.

### Threat B — Phone shaking / step fabrication

Mitigations:
- Accept that some sensor-level spoofing is possible.
- Cap economy acceleration.
- Use active-session cross-checks for bonus rewards.
- Keep competitive stakes low unless server authority is added.

### Threat C — GPS spoofing

Mitigations:
- Mock/test-location signal where available.
- Compare route distance with steps/cadence/time.
- Reject unrealistic acceleration/jumps.
- No bonus from GPS alone.

### Threat D — Manual health records

If HealthKit/Health Connect import is enabled:
- Android: inspect recording method; manual entries are not eligible for verified performance bonuses.
- iOS: inspect `HKMetadataKeyWasUserEntered` where provided.
- Imported/manual totals may be shown separately if product chooses, but should not silently receive high-trust bonuses.

### Threat E — Device clock manipulation

Mitigations:
- UTC timestamps.
- Monotonic session timing when possible.
- Clamp negative offline durations.
- Flag large future jumps.
- Later use server time for competitive/live-event economy.

### Threat F — Save editing

Single-player MVP:
- Accept that local saves can be tampered with.
- Use checksums/version validation to detect corruption, not to pretend perfect security.

Competitive future:
- Server-authoritative ledger required.

## 10. Trust scoring

Trust is a reward-quality score, not a moral judgment.

Possible signals:

Positive:
- coherent step cadence.
- distance compatible with step count.
- reasonable GPS accuracy.
- monotonic timestamps.
- sustained pedestrian movement.

Negative:
- vehicle-like speeds.
- repeated teleport jumps.
- distance with no step evidence.
- mock-location indicator.
- manual-entry metadata.
- impossible timestamp ordering.

Suggested categories:

```text
High confidence   → full optional session bonus
Medium confidence → base steps + limited bonus
Low confidence    → base locally-observed steps only
```

Never subtract previously spent progression because later confidence changes.

## 11. User-facing messaging

Bad:
> Cheating detected.

Better:
> This session did not contain enough reliable movement data for the optional activity bonus. Accepted steps still count.

The app should distinguish:
- unavailable sensor.
- denied permission.
- unreliable data.

Do not accuse the user when the cause may be poor GPS/device hardware.

## 12. Privacy architecture

Recommended data flow:

```text
Native sensor
   ↓
On-device normalization
   ↓
On-device plausibility checks
   ↓
Derived accepted steps / verified distance / bonus
   ↓
Game save
```

If telemetry backend exists:

```text
Game save / derived metrics
   ↓
Consent/telemetry filter
   ↓
Aggregate gameplay analytics
```

Raw GPS should not be part of ordinary analytics.

## 13. Cloud sync

When cloud save is introduced:
- Encrypt traffic in transit.
- Minimize sensitive fields.
- Do not use activity data for advertising targeting.
- Provide account/data deletion workflows required by platform policy.
- Maintain clear separation between game telemetry and health/activity-derived data.

## 14. Advertising

Strong recommendation: do not use movement/health data for ad personalization under any architecture.

If ads exist:
- Contextual/non-health-targeted only.
- Do not share HealthKit/Health Connect data with ad networks.
- Do not derive "fitness level" audience segments.

## 15. Monetization fairness

Do not sell:
- movement verification exemptions.
- Vitality generated from nowhere.
- speed multipliers.
- paid competitive advantage linked to fitness performance.

Prefer:
- cosmetics.
- decorative building sets.
- character customization.
- optional visual themes.

## 16. Children/minors

If the product is targeted toward children or likely to have a substantial minor audience, privacy and location requirements become significantly stricter. Before adding accounts, precise location, social systems, or ads, perform a dedicated age-rating/privacy review.

Do not assume general-audience policies are sufficient.

## 17. Accessibility and fairness

The game should be expandable to support different mobility modes.

Do not equate "healthier" with "faster."

Future integrations may include wheelchair movement metrics where platform APIs and design support them. Any conversion into gameplay currency must be documented and tested for fairness.

## 18. Injury-risk mitigation

Product design cannot guarantee physical safety, but it can avoid predictable bad incentives.

Required rules:
- No required daily maximum-effort challenge.
- No infinite speed multiplier.
- No escalating punishment for resting.
- No gameplay instruction encouraging exercise beyond personal capability.
- High-intensity reward caps.
- Optional sessions can end at any time without losing base steps.

## 19. Competitive features gate

Do not ship cash prizes, valuable prize competitions, or high-stakes global athletic leaderboards using client-only verification.

Before competitive stakes exist, require:
- server-authoritative event ledger.
- formal anti-cheat threat model.
- provider provenance policy.
- fraud monitoring.
- appeals/support process.
- store/legal review.

## 20. Release privacy checklist

Before every mobile release:

- [ ] Permission descriptions match actual use.
- [ ] Privacy policy matches actual collection.
- [ ] Location is not required for passive steps.
- [ ] Sensitive data is not sent to ad targeting.
- [ ] Health platform permissions are minimal.
- [ ] Health Connect declaration is accurate if used.
- [ ] HealthKit marketing/UI clearly support fitness use if used.
- [ ] Data deletion behavior is tested if accounts/cloud exist.
- [ ] Raw routes are not retained unintentionally.
- [ ] Debug sensor logs containing sensitive data are disabled in release builds.

## 21. Anti-cheat philosophy

For the initial single-player game, choose **frictionless legitimacy over aggressive policing**.

A player who cheats their own restoration progress harms the experience less than a false-positive system that rejects a legitimate walk.

The anti-cheat system becomes stricter only if competitive/social value makes cheating harmful to other players.