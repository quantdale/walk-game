# M8.5 Proposal — Runtime Ownership & Rollback Fidelity

**Status:** COMPLETE
**Change ID:** `m8.5-runtime-ownership-and-rollback-fidelity`  
**Planned-From:** `main@616924fcbe61bc50a1c7f064b0fe6fe00fb185ba`  
**Priority:** High  
**Target:** complete all unblocked correctness work required before meaningful device certification

## Problem

M8.4 established an engine-free transaction coordinator for activity delivery and proved the principal process -> commit -> provider-resolution ordering. The remaining runtime still lacks an explicit ownership model for asynchronous provider operations and provider/native lifetime, while adjacent rollback/presentation code contains several gaps that can violate the same exact-once and exact-durable-truth invariants.

The result is a mismatch between the repository's intended guarantees and reachable runtime behavior: stale Android resolutions can affect a newer claim, hung/late tasks can become ownerless, native provider state can survive a same-process runtime rebuild, a debug session path bypasses the coordinator, rollback can leave stale nested state, and failed rewards can remain visible after canonical state has reverted.

## Why now

The roadmap's next external milestone is device readiness, but the current documented environment cannot execute several editor/hardware gates. State integrity and movement correctness are explicitly higher priority than performance or content breadth. All M8.5 core work is testable at engine-free/static layers and makes later device validation materially more trustworthy.

## Goals

1. Give every provider operation a clear runtime owner and deterministic cancellation/abandon behavior.
2. Give every provider instance an explicit, idempotent teardown contract that releases native monitoring/session state before the runtime graph is replaced.
3. Make stale/repeated Android prepared-delivery resolution harmless by binding resolution to claim identity.
4. Route every active-session completion/failure/no-result path through one transaction protocol.
5. Make player-facing success copy and runtime feedback reflect proven durable state only.
6. Make in-place persistence rollback reconstruct the durable object graph exactly while preserving identity of surviving objects.
7. Harden dedup rebuild against duplicate/corrupt serialized entries so capacity compaction can never reopen a credited key.
8. Add deterministic regression coverage for the above behaviors in the standalone/headless gate wherever possible.
9. Update architecture/testing/mobile documentation and record the provider-lifetime decision in an ADR.

## Non-goals

M8.5 MUST NOT expand into:

- Region 2 or post-Ashfall content.
- HealthKit or Health Connect.
- GPS/Fused Location/Core Location feature expansion.
- cloud sync, accounts, telemetry, multiplayer, leaderboards, or server authority.
- reward-economy rebalance unrelated to correctness.
- art/UI overhaul.
- Addressables migration.
- speculative mobile optimization without measured device evidence.
- bypassing Unity licensing, Android module installation, signing, Xcode/macOS, UAC, or physical-device requirements.

Passive movement must remain usable without GPS.

## Impact surface

Expected implementation impact includes, but is not limited to:

- `Assets/WalkGame/Activity/IActivityProvider.cs` and provider lifecycle abstractions.
- `DebugActivityProvider`, `UnavailableActivityProvider`, Android and iOS providers.
- Android reconciler/claim identity helper.
- native Android and iOS teardown bridge calls as required.
- `GameHost`, `ActivityTicker`, `ExpeditionController`, `UiComposer`, `MotionPermissionCoordinator`, `TaskObservation` or a new engine-free operation helper.
- `ActivityTransactionCoordinator` only where necessary to make all active-session paths converge.
- `FeedbackController` / UI presentation for durability-gated result and audio rollback truthfulness.
- `ProfileStateCopier`, `CreditedActivityKeys`, `SaveValidator`.
- corresponding EditMode/headless and PlayMode tests.
- ADR 0011 and architecture/mobile/activity/testing/status docs.

The exact implementation shape is intentionally not prescribed where multiple safe designs exist. The normative behaviors in `specs/runtime-ownership/spec.md` are mandatory.

## Compatibility

Prefer no save-schema bump. The campaign repairs runtime ownership and canonicalizes already-supported dedup data rather than adding a new persisted contract. If implementation introduces any serializer-visible field or changes persisted semantics incompatibly, follow `docs/DATA_MODEL.md`: increment schema version, write deterministic migration, extend `ProfileStateCopier`, fixtures, and save tests.

Provider API changes are internal source compatibility changes and all implementations must migrate together.

## Risks

### Cancellation races

A cancellation request can race a provider completion. The design must guarantee exactly one terminal owner: process/resolve the completed delivery, or abandon/reject/stop it. Never both, never neither.

### Native callback races

Teardown may race native callbacks. Provider generations/disposed state must make late callbacks harmless. Do not depend on Unity object existence from a background callback.

### Movement loss

Cancellation/teardown must not acknowledge movement that was not durably committed. If provider-private movement was staged but not committed, it must remain retryable through provider restore or process-restart reconstruction.

### Double credit

Claim identity, dedup repair, and rollback changes must preserve all M8.4 exactly-once scenarios. No fix may convert stale callbacks into duplicate reward paths.

### Over-abstraction

Do not add a general async framework. Introduce the smallest reusable ownership primitive needed to make activity/permission lifecycle testable and deterministic.

## Success criteria

The change is complete only when:

- every mandatory scenario in the normative spec has automated coverage at the lowest viable layer;
- all locally available pre-existing gates pass with fresh evidence;
- all provider implementations obey the new lifetime/ownership contract;
- no independent active-session completion sequence remains outside the sanctioned coordinator protocol;
- failed/reverted rewards and settings cannot leave success-only presentation state behind;
- rollback graph fidelity and dedup corruption tests prove the repaired invariants;
- documentation matches implementation and ADR 0011 records the architecture decision;
- editor/device tiers are either actually run and evidenced or explicitly marked UNVERIFIED.

## Exit direction

After M8.5, if no new Critical/High correctness defect is found, the next campaign should preferentially be **real Unity/device certification and playtest readiness**, not another speculative headless hardening cycle.
