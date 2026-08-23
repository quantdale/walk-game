# ADR 0001 — Standalone domain verification harness

## Status

Accepted

## Context

The architecture (TECHNICAL_ARCHITECTURE section 4, AGENT_EXECUTION_GUIDE section 7)
demands that core calculations live in plain C# assemblies with no engine dependency:
`WalkGame.Core`, `WalkGame.Building`, `WalkGame.Gameplay`, `WalkGame.Activity`,
`WalkGame.Persistence`, and content. Unity's EditMode test runner is the documented
test surface, but it requires an installed, licensed editor. The development
environment used to bootstrap this repository has no Unity installation.

Without a runnable verification loop, the highest-value logic (vitality ledger,
step deduplication, offline production caps, save atomicity/recovery, placement
validation) would ship untested - violating the definition of done.

## Decision

All engine-free assemblies are compiled twice:

1. Inside Unity via asmdefs (`noEngineReferences: true` where applicable).
2. Outside Unity by `verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj`,
   which links the exact same `.cs` files and runs them under NUnit on the installed
   .NET SDK (net8.0, pinned to C# 9 to match Unity's scripting runtime).

EditMode tests under `Assets/WalkGame/Tests/EditMode/` are written against NUnit only
(no `UnityEngine.TestTools`) so the same files execute in both environments.
`dotnet test verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj` is the
canonical local gate; CI should run both once an editor-capable runner exists.

## Consequences

- Domain behavior is verified deterministically without an editor or device.
- Test code must avoid Unity-specific test APIs; PlayMode-only coverage is deferred
  until editor access exists.
- Serialization uses Newtonsoft.Json (Unity package + NuGet equivalent) so save logic
  round-trips identically in both worlds.
