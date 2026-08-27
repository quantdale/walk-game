# iOS build requirements — Walk Game

Applied by `IosPedometerPlistPostprocessor` when the deterministic
`WalkGame.EditorTools.WalkGameEditorTools.BuildIosXcodeDevelopment` entry point
generates the Xcode project:

## Info.plist keys

- `NSMotionUsageDescription`:
  > Walk Game uses motion data to turn your walking and running into restoration progress in your game world.

- `NSLocationWhenInUseUsageDescription` (only once Phase 4C Expeditions ship; do not add before then):
  > Used only during an Expedition you start, to verify distance for capped activity bonuses. Passive steps never need location.

## Frameworks

Unity links CoreMotion automatically via the .mm plugin's `#import <CoreMotion/CoreMotion.h>`.

## Certification command

On macOS, set `UNITY_EDITOR_PATH` to the pinned Unity `6000.3.4f1` editor and run
`scripts/build-ios-xcode.ps1`. The wrapper requires Xcode 26 or later and an iOS
26 or later SDK for current App Store submission readiness, records separate Unity
and Xcode logs plus source/tool/output hashes, and fails closed if the generated
bundle identifier, CoreMotion bridge, or motion-usage key is missing. Signing and
device installation are opt-in (`-Sign -SigningTeamId -DeviceSerial`) and no
provisioning material belongs in Git.

The current Apple SDK floor is documented by [Apple's upcoming requirements](https://developer.apple.com/news/upcoming-requirements/).

## Review gates

- Permission prompts are triggered from gameplay context only (never first launch).
- Denying motion access keeps building/exploring fully playable; the debug provider
  remains available in development builds to exercise restoration flows.
