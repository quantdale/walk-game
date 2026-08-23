# Physical-Device Certification Checklists

Executable preconditions and step-by-step evidence capture for the DEVICE evidence
tier. Nothing in this file is claimed as passed until a run against real hardware
produces the listed artifacts. Record each completed row with a date, device model,
OS version, build ID, and artifact path in `IMPLEMENTATION_STATUS.md`.

Evidence rule: a case is PASS only with its artifact (screenshot/logcat/trace).
Anything else stays UNVERIFIED.

---

## Android checklist

Preconditions:

1. Development APK built from the pinned editor via
   `scripts/build-android-development.ps1` (`Builds/Android/WalkGame-dev.apk`).
2. `adb devices` shows exactly the device under test (no emulator attached).
3. `scripts/verify-android-smoke.ps1` has passed on this same APK.

| # | Case | Steps | Evidence artifact | Pass criteria |
| --- | --- | --- | --- | --- |
| A1 | First launch | Clear data (`pm clear`), launch offline (airplane mode) | logcat + screenshot | Bootstrap scene renders; fresh profile; no fatal exceptions |
| A2 | Motion permission first ask | Tap "Enable" on the HUD banner | screen recording | OS dialog appears once; grant → banner disappears |
| A3 | Permission denial | Deny at dialog; relaunch app | screenshot + logcat | Banner explains state; building/Explore still work; no crash |
| A4 | Permission granted later | Enable in App Settings, return | screenshot | Passive steps resume without reinstall/re-login |
| A5 | Baseline establishment | With counter previously credited, reboot phone before walking | logcat `Step counter` lines | No negative credit after reboot; new delta credits once |
| A6 | Walking sample | Walk 200+ known steps with screen off, reopen app | Vitality HUD before/after screenshot | Balance increases ~1:1 within plausibility cap, exactly once |
| A7 | Background during walk | Start walk, background app 10 min, resume | logcat + balance deltas | Steps earned while backgrounded credit once, not zero and not twice |
| A8 | Force stop mid-walk | Force stop during walk, walk more, relaunch | logcat + balance deltas | Interrupted-session recovery message; steps across kill credit once |
| A9 | Phone reboot | Reboot device between two walking sessions | logcat | Counter reset re-baselines; no negative or duplicate credit |
| A10 | Counter reset by system | `dumpsys sensorservice` reset or battery pull equivalent | logcat | Same as A9 |
| A11 | Expedition start/finish | Full Walk Expedition ≥ 5 min outdoors | Expedition UI screenshots + save inspection | Result credited once; passive suppressed during session |
| A12 | Location denied | Start Run Expedition with location denied | UI screenshot | Base steps still count; no bonus; no GPS prompt loop |
| A13 | Duplicate-credit probe | Repeat A6/A11 twice with identical conditions | ledger transaction dump (`recentVitalityTransactions`) | No duplicate reason-code entries for one movement window |
| A14 | Battery/thermal sample | 20 min Builder+Explore session, full brightness off, record battery % | Battery Historian / `dumpsys batterystats` export | No thermal shutdown; record drain rate for the perf baseline |
| A15 | Release-shape sanity | Install a non-development release build when configured | logcat | No debug menu; warning-level logging only |

Run A2–A4 only after confirming the device actually exposes
`TYPE_STEP_COUNTER` (`adb shell pm list features | grep stepcounter`). Emulators
without that feature must use the unavailable-provider fallback path instead.

---

## iOS checklist

Preconditions: Xcode project generated from the pinned editor on macOS, signed,
device provisioned. All of the following remain UNVERIFIED until run.

| # | Case | Steps | Evidence artifact | Pass criteria |
| --- | --- | --- | --- | --- |
| I1 | Xcode archive/build | Build to device | Xcode build log | Compiles clean; NSMotionUsageDescription present in Info.plist |
| I2 | Core Motion first request | Fresh install, tap "Enable" | screen recording | CMPedometer authorization prompt appears once |
| I3 | Denied permission | Deny; exercise game | screenshots | Gameplay unaffected; banner copy neutral |
| I4 | Historical reconciliation | Walk with app closed 30+ min, open | balance delta + logs | Historical window credited once; cursor advances past window |
| I5 | Live Expedition | Walk Expedition with live updates | UI + logs | Steps accumulate; finish pays once |
| I6 | Background/resume mid-window | Background during walk, resume | logs | Overlapping query windows never double-pay (dedup keys) |
| I7 | Exactly-once restart probe | Kill app right after finishing an expedition, relaunch | logs + balance | Session id dedup prevents second payment |

---

## Recording results

For each executed case append to the campaign notes:

```text
<case-id> | <date> | <device model> | <os> | <build sha> | PASS/FAIL | <artifact path>
```

A FAIL on any exactly-once case (A5–A10, I4–I7) blocks the release gate.
