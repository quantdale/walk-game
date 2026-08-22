# iOS build requirements — Walk Game

Applied by the post-build editor hook (`WalkGameEditorTools.AddPedometerUsageDescription`)
when generating the Xcode project:

## Info.plist keys

- `NSMotionUsageDescription`:
  > Walk Game uses motion data to turn your walking and running into restoration progress in your game world.

- `NSLocationWhenInUseUsageDescription` (only once Phase 4C Expeditions ship; do not add before then):
  > Used only during an Expedition you start, to verify distance for capped activity bonuses. Passive steps never need location.

## Frameworks

Unity links CoreMotion automatically via the .mm plugin's `#import <CoreMotion/CoreMotion.h>`.

## Review gates

- Permission prompts are triggered from gameplay context only (never first launch).
- Denying motion access keeps building/exploring fully playable; the debug provider
  remains available in development builds to exercise restoration flows.
