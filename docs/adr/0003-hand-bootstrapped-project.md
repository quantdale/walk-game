# ADR 0003 — Hand-bootstrapped project structure

## Status

Accepted

## Context

Phase 0 requires a Unity 6.3 LTS URP project. The bootstrap environment has no Unity
installation and no license; installing the editor and activating a Personal license
require interactive sign-in. Blocking all implementation on editor availability would
have left the repository documentation-only.

Unity regenerates most `ProjectSettings/*.asset` files with defaults when missing, and
it generates `.meta` files for untracked assets on import - but scenes reference script
GUIDs, so scripts used by scenes need stable, committed `.meta` files to avoid GUID
churn across machines.

## Decision

The project structure was authored by hand:

- `ProjectSettings/ProjectVersion.txt` pins the editor line (6000.3.x); Hub resolves
  the exact revision on first open.
- `Packages/manifest.json` pins URP, Input System, Test Framework, Newtonsoft.
- Every `.cs` file carries a deterministic `.meta` (GUID derived from repo-relative
  path) so hand-authored scene YAML can reference them safely.
- `Bootstrap.unity` contains only the GameHost composition root; presentation rigs,
  lighting, and UI are built programmatically at runtime, keeping scenes content-only
  per TECHNICAL_ARCHITECTURE section 6.
- `WalkGame/Setup/Configure URP and Input System` (editor menu) creates and assigns the
  URP asset and switches input handling on first open; run it once after opening.

## M8.8 amendment — generated-state provenance and platform build identity

The hand-bootstrapped source remains the authority until a licensed editor is available;
agents must not invent serialized Unity YAML to make a static gate appear complete. A
licensed first-import run may materialize only Unity-generated canonical state (the
resolved package lock, canonical project settings, and the URP asset), and the semantic
compile evidence must bind the source SHA, dirty state, timestamps, editor identity, log,
and any accepted canonical mutations. A second clean checkout and idempotent setup are
required before those generated files are treated as reproducible build inputs.

The Android release-shaped entry point preserves minSdk 26 and targets API 36+, verifying
the generated APK manifest. The iOS entry point binds bundle identifier
`com.quantdale.walkgame` and minimum deployment target 16.0, while the macOS wrapper
records Unity/Xcode/SDK/project/build evidence and requires Xcode 26+/iOS 26+ for any
current App Store readiness claim. Neither external lane is claimed as verified without
its editor/toolchain and device evidence.

## Consequences

- First open in Unity 6.3 LTS should: let it recompile/import, then run the setup menu
  item, then open `Assets/WalkGame/Core/Bootstrap.unity` and press Play.
- Scene YAML is minimal and may be normalized by Unity's serializer on first save; that
  is expected and harmless.
- Any future engine pin change must be recorded here and in ProjectVersion.txt.
