# Walk Game — Activity & Reward System

## 1. Goals

The activity system must satisfy four competing requirements:

1. **Walking remains the universal baseline.**
2. **Running/endurance can earn additional recognition.**
3. **Speed must not become an unsafe infinite multiplier.**
4. **Vehicle travel and fabricated data must not become the optimal strategy.**

The system should reward movement patterns rather than punish players who move slowly, rest, use different devices, or cannot run.

## 2. Terminology

- **Step** — device-reported pedestrian step.
- **Vitality** — game resource generated from accepted movement.
- **Passive movement** — normal daily steps recorded without starting an in-game session.
- **Active Session / Expedition** — user explicitly starts a walking/running session in the app.
- **Verified Session** — active session that passes minimum sensor consistency checks.
- **Movement Bonus** — capped extra Vitality granted for selected session characteristics.
- **Trust Score** — internal confidence score for a session; never shown as an accusation.

## 3. Base Vitality

Prototype baseline:

```text
BaseVitality = AcceptedSteps × 1
```

This is intentionally transparent. Do not add hidden per-user exchange rates.

### Daily soft cap

Do not hard-stop ordinary step rewards at a typical daily target. Instead use a diminishing bonus curve only if economy inflation becomes a problem.

Example future curve:

```text
0–15,000 steps:     1.00 Vitality / step
15,001–30,000:      0.75 Vitality / step
30,001–50,000:      0.50 Vitality / step
50,001+:            0.25 Vitality / step
```

This is a balancing lever, not an MVP requirement. Extreme legitimate hikers should still receive progress; they simply should not destroy the entire economy in one day.

## 4. Passive movement

Passive movement should require only motion/activity permission, not GPS.

Benefits:
- Low battery impact.
- Works for normal daily life.
- Avoids collecting location when not needed.
- Makes the game usable without active workouts.

Passive movement earns:
- Base Vitality.
- Lifetime step milestones.
- Consistency progress.

Passive movement does **not** earn speed/route bonuses because the app cannot safely verify pace from sparse background data.

## 5. Active Expeditions

An Expedition is optional and explicitly started by the player.

Possible session types:
- Walk.
- Run.
- Long Walk / Hike later.

An active session can collect:
- Step delta.
- Elapsed time.
- Moving time.
- Distance estimate.
- GPS samples while session is active and permission exists.
- Pace/cadence where platform supports it.
- Sensor availability/quality.

The UI should allow the player to lock the screen and continue the session where supported. Do not require constant visual interaction while moving.

## 6. Reward dimensions

### A. Steps — universal

Already paid as Base Vitality.

### B. Distance — Explorer bonus

Reward sustained distance with a **bounded** session bonus.

Prototype:

```text
ExplorerBonus = min(500, floor(VerifiedDistanceKm × 50))
```

Example:
- 2 km → +100.
- 5 km → +250.
- 10 km → +500 cap.

Do not pay distance twice through both estimated steps and unbounded GPS reward.

### C. Duration — Endurance bonus

Reward sustained verified moving time rather than raw elapsed session time.

Prototype tiers:

```text
20 min moving → +50
40 min moving → +100
60 min moving → +150
90 min moving → +200 cap
```

This rewards marathon-style endurance without requiring high speed.

### D. Consistency — Rhythm bonus

A session can earn a small bonus for sustained pedestrian cadence without extreme spikes.

Prototype:

```text
if verifiedMovingMinutes >= 10 and cadenceConsistency >= threshold:
    RhythmBonus = 50
else:
    0
```

Do not rank cadence globally. Device support varies.

### E. Personal improvement — Growth bonus

Compare the player primarily to their own rolling history, not elite athletic standards.

Possible metric:

```text
recentBaseline = median(valid session metric over previous 28 days)
if current session shows 5–15% improvement and trust is high:
    small capped GrowthBonus
```

Never create rewards that require repeated personal records.

## 7. Speed policy

### Never use

```text
Reward ∝ topSpeed
```

or

```text
Reward ∝ 1 / pace
```

without caps.

These formulas create incentives to run beyond ability, bike, drive, or fake GPS.

### Recommended approach

Use speed only as a **classification and validation signal**.

Example categories (illustrative, not medical thresholds):
- likely walking.
- likely running.
- implausible pedestrian speed.

The game can award the same capped "Tempo" badge/bonus across a broad normal running band rather than paying more for every additional km/h.

Example:

```text
if session classified as sustained_run and trustScore >= 0.8:
    TempoBonus = 100
```

No extra reward for running 20 km/h instead of 12 km/h.

## 8. Session reward formula

Initial prototype:

```text
SessionReward =
    BaseVitalityFromSteps
  + ExplorerBonus
  + EnduranceBonus
  + RhythmBonus
  + TempoBonus
  + GrowthBonus
```

Then apply:

```text
BonusReward = min(BonusReward, SessionBonusCap)
```

Suggested initial `SessionBonusCap = 750 Vitality`.

The cap must be balance-tuned.

## 9. Bonus frequency caps

To prevent unsafe grinding:

- Distance bonus: normal per-session cap.
- Tempo bonus: at most 1–2 meaningful awards/day.
- Growth bonus: at most once/day.
- Personal-best cosmetic achievement: infrequent, no major economy impact.

After caps are reached, steps still earn normal Base Vitality.

## 10. Walker parity

A player should never feel forced to run to progress.

Design target:
- Walking determines most long-term Vitality.
- Running bonuses accelerate progress modestly, not exponentially.
- A highly active walker can achieve everything a runner can, except running-specific cosmetics/badges.

Good exclusive running rewards:
- Cosmetic trail effect.
- Runner monument.
- Profile badge.
- Decorative banner.

Bad exclusive running rewards:
- Region unavailable to walkers.
- Strongest production building.
- Permanent huge Vitality multiplier.

## 11. Endurance recognition

Marathon-style players should be celebrated for distance/duration rather than speed.

Achievement examples:
- 5 km Expedition.
- 10 km Expedition.
- 21.1 km lifetime-session achievement.
- 42.2 km lifetime or single-session achievement if reliably verified.
- 100 km cumulative Expedition distance.

A single marathon achievement should be a prestige/cosmetic event, not a massive competitive advantage.

## 12. Sprint recognition

True sprinting is difficult to verify safely on a phone and easy to incentivize badly.

Therefore the launch game should **not** contain "run as fast as possible" challenges.

If a later sprint feature is added:
- It should require an explicitly started short session.
- It should use a broad qualifying threshold, not leaderboard top speed.
- It should have a strict attempt/reward cap.
- The game should not require screen interaction while sprinting.
- It should be reviewed as a separate safety feature before shipping.

## 13. Suspicious movement handling

Do not build a punitive anti-cheat experience into the core UX.

### Signals

Possible signals include:
- GPS speed inconsistent with pedestrian activity.
- Distance impossible relative to steps/cadence.
- Large teleport jumps.
- Low GPS accuracy combined with extreme displacement.
- Android mock-location indication where available.
- Sensor timestamp anomalies.
- Imported/manual records if health-platform integration is enabled.
- Repeated identical synthetic session patterns.

### Trust score concept

```text
trustScore ∈ [0, 1]
```

A session starts neutral and gains/loses confidence based on signals.

Suggested behavior:

```text
trust >= 0.80 → full eligible bonuses
0.50–0.79    → base step reward + reduced/zero session bonus
< 0.50        → base locally-observed steps only; no performance bonus
```

Do not display "cheater detected." Show something neutral such as:

> This session did not contain enough reliable movement data for an activity bonus. Your accepted steps still count.

## 14. Vehicle resistance

A car typically creates:
- Distance with low/no step cadence.
- Sustained speed outside ordinary pedestrian bands.
- Smooth GPS displacement unlike walking/running.

Therefore:
- Never reward GPS distance alone.
- Require pedestrian step/cadence evidence for active-session bonuses.
- Reject/discount intervals that exceed configured pedestrian plausibility thresholds.
- Ignore large GPS jumps.

Do not attempt perfect cheat detection. The objective is to make legitimate movement the easiest way to earn rewards.

## 15. Time handling

Activity rewards are vulnerable to clock changes.

Rules:
- Store timestamps in UTC.
- Store local date/time-zone offset separately for daily presentation.
- De-duplicate activity by provider sample/session identifiers when available.
- Maintain last processed cumulative step counter and device reboot handling on Android.
- Never award negative Vitality when provider corrections occur.
- Corrections should reconcile future deltas, not remove already spent restoration progress.

## 16. Android cumulative counter handling

`TYPE_STEP_COUNTER` reports steps since reboot while sensor has been active.

Persist:

```text
lastRawCounter
lastCounterTimestamp
currentBootEpoch/boot marker if available
creditedLifetimeFromProvider
```

If new raw counter >= prior raw counter:

```text
delta = new - prior
```

If counter resets because of reboot/provider reset:
- Establish new baseline.
- Do not interpret reset as negative steps.

Delivery durability (ADR 0009): the folded delta is staged as a prepared claim
and only dropped after the enclosing profile commit proves it durable. A
rejected commit returns claimed steps to the pending stream for exactly one
retry; the runtime baseline may transiently sit ahead of the rolled-back
persisted cursor because folds credit only raw increases from that baseline and
a restart re-seeds conservatively from the persisted value. Reboot/anomaly
rebaselining never converts restored pending steps into huge, negative, or
double rewards. Expedition completion holds the session's base steps until its
commit resolves; rejection returns them to the passive stream.

Orchestration durability (ADR 0010): a lifecycle autosave may durably persist
the `activeSession` marker while an Expedition runs. If the completion commit
then fails, the coordinator reverts the profile from disk (restoring that marker)
and immediately repairs it in memory (`RecoverInterruptedSession`) after rejecting
the provider completion, so the returned base movement is not suppressed in the
same process. The repair converges durably on the next successful commit and
remains reconstructible via boot recovery if the process dies first. The same
repair applies after a passive `DurableMutation` revert that resurrects a stale
marker, and no-result stop paths (`StopSessionAsync` fault/cancel/null) durably
close the marker through the same transaction. Fatal persistence loss during
completion or passive reconciliation fails closed and never fabricates reward.

## 17. iOS historical reconciliation

Core Motion `CMPedometer` provides up to seven days of historical pedestrian data.

On app launch:
- Query from last successful sync timestamp, bounded by available history.
- Credit only unprocessed intervals.
- Persist new sync cursor.
- Never re-credit the same time interval.

Delivery durability (ADR 0009): preparation consumes nothing provider-private,
and both resolutions are no-ops — a failed application commit rewinds the
durable successful-sync cursor with the profile rollback, leaving exactly that
window queryable again; durable dedup/cursor state suppresses anything that did
commit. Completed sessions recover through the same history path because the
rolled-back profile never advances the cursor past an uncredited window.

If the app has not opened beyond the available history window, explain that only available device history can be imported unless later HealthKit support is enabled.

## 18. Debug provider

Before native integration, build an `IActivityProvider` fake/debug implementation.

The debug provider mirrors the production delivery contract (ADR 0009) so
standalone tests exercise transaction semantics: passive reads stage movement
instead of zeroing the fake counter, rejected deliveries restore it, session
progress leaves the passive stream at stop and is held until its commit
resolves.

Required debug controls:
- Add 1,000 steps.
- Simulate 5 km walk.
- Simulate 5 km run.
- Simulate device reboot counter reset.
- Simulate time-zone change.
- Simulate suspicious vehicle session.
- Simulate missing cadence/GPS.

All game logic must depend on the activity abstraction, never directly on native APIs.

## 19. Safety UX

- No prompts to look at the screen while actively running.
- Active-session milestone notifications should be optional/audio/haptic where appropriate.
- Do not tell users to ignore pain, fatigue, weather, surroundings, traffic, or medical advice.
- "Rest day" does not break lifetime progress.
- Never frame increasingly extreme exercise as necessary to save the world.

## 20. Accessibility

Long-term architecture should permit additional movement sources without redefining the economy.

Examples for future evaluation:
- Wheelchair distance/push counts through appropriate platform APIs.
- Indoor treadmill sessions.
- Wearable-imported activity.

Do not silently equate all movement types to walking; use explicit provider adapters and fair conversion rules.

## 21. Balancing test matrix

Before public release test at least these simulated player profiles:

1. 2,000 steps/day casual.
2. 5,000 steps/day moderate.
3. 10,000 steps/day active.
4. 20,000 steps/day very active.
5. 5 km runner 3×/week.
6. Long-distance walker/hiker.
7. User opening app every day.
8. User opening once per week.
9. User with no location permission.
10. User with motion permission denied.

The game must remain playable and understandable for all, with progression speed differing but not content eligibility based on athletic ability.

## 22. Metrics to validate

Track game-level aggregates such as:
- Accepted steps/day bucket.
- Vitality earned/spent.
- Session bonus frequency.
- Percent of users starting Expeditions.
- Percent of sessions denied optional bonus for low confidence.
- Restoration time by activity bucket.

Avoid retaining raw location routes by default. Prefer deriving distance/trust on-device and storing only the minimum results required for gameplay.