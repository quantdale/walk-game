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

## Consequences

- First open in Unity 6.3 LTS should: let it recompile/import, then run the setup menu
  item, then open `Assets/WalkGame/Core/Bootstrap.unity` and press Play.
- Scene YAML is minimal and may be normalized by Unity's serializer on first save; that
  is expected and harmless.
- Any future engine pin change must be recorded here and in ProjectVersion.txt.

## M8.8 amendment — deterministic import & first-import provenance

M8.8 clarified that the hand-bootstrap still intentionally leaves `Assets/Settings/URP-HighFidelity.asset` un-tracked until a licensed Unity `6000.3.4f1` editor can generate it deterministically. `WalkGameEditorTools.ConfigureUrp` is idempotent (loads existing asset else creates with mobile-first defaults and assigns `GraphicsSettings.defaultRenderPipeline`/`QualitySettings.renderPipeline`). `scripts/verify-unity-compile.ps1` now proves semantic compilation, rejects stale/compiler-error evidence, and reports `mutatedFiles`/`postDirty` so the first editor import's diff is captured and bound to build provenance. The preferred next step on a licensed host is to run the compile gate, capture the `URP-HighFidelity.asset` diff, classify it as canonical trackable state, prove second-run idempotence, and commit the stable asset so clean checkouts become byte-identical to the certified build. Until that host exists, no opaque `.asset` is hand-fabricated; the tier remains **UNVERIFIED** with the exact blocker.
