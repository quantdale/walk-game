# ADR 0004 — Platform assemblies compile on every target with source-level guards

## Status

Accepted

## Context

`WalkGame.Platform.Android` and `WalkGame.Platform.iOS` were declared with
`includePlatforms: ["Android"] / ["iOS"]`. `WalkGame.App` references both because its
composition root (`GameHost.CreateProvider`) selects providers per platform.

That combination cannot compile on any single target:

- Building for Android excluded the iOS assembly, so `WalkGame.App` failed to resolve a
  referenced assembly (and vice versa), including in the Editor.
- Separately, the Android assembly declared `noEngineReferences: true` while its provider
  used `AndroidJavaObject`/`AndroidJavaClass`, which is a guaranteed compile failure on
  device builds.

The first real Unity bring-up would have hit both immediately; both were found by static
audit before an editor was available.

## Decision

- Both platform asmdefs now compile on **every** platform (`includePlatforms: []`) and
  reference the engine (`noEngineReferences: false`, required by Android JNI interop).
- Their sources are wrapped in `#if UNITY_ANDROID && !UNITY_EDITOR` /
  `#if UNITY_IOS && !UNITY_EDITOR`, so no platform-specific type can leak into an
  unsupported compilation target - source-level guarantees are stricter than assembly
  filtering.
- Provider construction stays inside `GameHost.CreateProvider`; only genuine bridge/
  packaging failures fall back to the debug provider. A missing runtime permission is a
  normal provider state handled by the permission UI, never a silent debug fallback.

## Consequences

- The project compiles as-is in Editor, Android, iOS, and standalone targets once an
  editor is available; empty platform shells are harmless.
- Any new platform file must carry the same guard defines.
- Engine-referencing platform adapters stay out of the standalone verification harness,
  so harness coverage of platform behavior flows through engine-free classes
  (`AndroidCounterReconciler`, `IosHistoryWindowPlanner`, `MotionPermissionCoordinator`)
  rather than the MonoBehaviours/JNI shells.
