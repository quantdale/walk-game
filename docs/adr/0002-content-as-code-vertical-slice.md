# ADR 0002 — Content-as-code for the vertical slice

## Status

Accepted

## Context

TECHNICAL_ARCHITECTURE section 12 prescribes ScriptableObjects for immutable content
definitions. Authoring SOs was impractical while bootstrapping the project outside the
Unity editor, and the vertical slice's content (one region, nine buildings, fifteen
projects, four producers) benefits from being reviewable and diffable as plain code.
DATA_MODEL.md and WORLD_BUILDING_SYSTEM.md require that persistent IDs be stable API;
the storage medium of definitions is secondary to that invariant.

## Decision

Ashfall Basin content lives in `Assets/WalkGame/Content/Catalog/AshfallBasinCatalog.cs`
as plain C# implementing `IContentCatalog`. Services consume only the catalog interface,
never concrete content. Deterministic integrity tests (prerequisite DAG resolution,
footprint fit/reserved-area checks, reward-target resolution, full scripted playthrough)
gate every content change in the standalone harness and in Unity's Test Runner.

A ScriptableObject surface may later wrap this catalog (SOs materializing the same
plain records) without changing any service, ID, or save schema. If that happens, the
integrity tests must run against SO-authored data before it ships.

## Consequences

- Content changes require recompilation instead of asset-only changes; acceptable for
  one region.
- Designers cannot author content without code access until the SO wrapper exists.
- The integrity tests are the migration gate: they must pass identically before and
  after any move to ScriptableObjects.
