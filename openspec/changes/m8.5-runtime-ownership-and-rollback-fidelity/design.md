# M8.5 Design — Runtime Ownership & Rollback Fidelity

**Status:** ACTIVE design constraints  
**Change:** `m8.5-runtime-ownership-and-rollback-fidelity`

## 1. Design thesis

M8.5 extends the transaction guarantee from **data ownership** to **operation ownership**.

M8.4 answers: “When a provider has prepared movement, what ordering makes reward/cursor persistence and provider resolution exactly-once?”

M8.5 must additionally answer:

- Who owns an outstanding provider task?
- What happens if its Unity owner disappears?
- What happens if the provider instance is replaced?
- What happens when timeout and completion race?
- Which delivery/session identity is being resolved?
- Which presentation state is allowed to survive rollback?

The implementation must make these answers explicit and testable.

## 2. Non-negotiable invariants

### I1 — one runtime generation owns one provider instance

A composed gameplay runtime has one provider generation. Before `GameHost` replaces/drops the provider graph, that provider is shut down idempotently. Late operations from an older generation cannot mutate, resolve against, or refresh a newer generation.

### I2 — every asynchronous provider operation reaches one terminal ownership state

For every start, poll, stop, passive prepare, capability refresh, or permission request, exactly one terminal path owns the result:

1. completion is adopted and processed by the current owner; or
2. operation is canceled/invalidated and any provider-private staged state is explicitly abandoned/restored/stopped; or
3. fatal persistence has moved the app to fail-closed state and provider teardown owns cleanup without fabricating a durable resolution.

Never both process and abandon the same operation. Never leave it with no owner.

### I3 — cancellation is not acknowledgment

Canceling UI/runtime interest does **not** prove a movement delivery was durably committed. Prepared movement must remain retryable unless durable state proves consumption.

### I4 — resolution is identity-bound

A provider resolution affects only the delivery/session identity named by that resolution. Null, unknown, repeated, and stale identities are harmless no-ops.

### I5 — one active-session completion protocol

Every path that closes an Expedition-like provider session delegates reward processing, commit outcome, provider resolution, and rollback-marker repair to the same transaction protocol. Debug/test conveniences may inject facts but must not recreate the transaction sequence.

### I6 — canonical disk truth wins presentation truth

Success-only HUD copy, reward summaries, celebration cues, and runtime-applied settings cannot claim a state that the failed commit rolled back.

### I7 — rollback graph equals durable graph

After `ProfileStateCopier.CopyInto(source, target)`, serializer-visible target state equals source state, including removal of target-only nested dictionary entries, while references to surviving canonical subobjects remain stable where the architecture requires identity preservation.

### I8 — dedup membership matches canonical entries

After rebuild/repair, `CreditedActivityKeys.entries` is deterministic, unique, bounded, and `_set` membership is exactly equal to the surviving entries. Capacity trimming can never make a surviving credited key appear uncredited.

## 3. Provider lifecycle contract

The executor may choose `IDisposable`, an explicit `Shutdown`/`StopAsync`, a dedicated provider-lifetime interface, or an equivalent small design, but it must satisfy all requirements below.

The provider lifecycle MUST:

- be idempotent;
- prevent new operations after shutdown from starting native work;
- stop native passive monitoring/listeners owned by that provider generation;
- stop/abort a transient active native session when the provider is being discarded;
- cancel/invalidate provider-owned pending callbacks/requests where supported;
- make late native callbacks harmless;
- not acknowledge a prepared activity delivery merely because the provider is shutting down;
- preserve restart reconstruction from the durable cursor/native absolute source.

### Android

The C# adapter must use the Kotlin bridge's stop-monitoring capability during teardown. If an active debug/native session abstraction has provider-private claim state, teardown must restore/reconstruct rather than silently consume it.

### iOS

The C# adapter must stop live CoreMotion updates during teardown. Native callback/request bookkeeping must not retain a discarded provider generation indefinitely. A newly composed provider in the same process must not inherit an old live-session `AlreadyRunning` condition caused solely by leaked teardown.

### Debug / unavailable providers

They must implement the same public lifecycle semantics so headless tests can exercise the real contract rather than a special fake-only path.

## 4. Operation ownership and cancellation

Prefer standard `CancellationToken` propagation on asynchronous provider methods if it remains practical across Unity 6.3 / C# 9 and the native callback bridge. An equivalent operation lease/generation design is acceptable only if it can be deterministically tested.

Do not solve this by adding arbitrary more seconds to timeouts.

An ownership primitive, if introduced, should carry only what is needed:

- owner/runtime generation;
- cancellation/invalidated state;
- terminal transition that can succeed once;
- optional reason for diagnostics.

It should remain engine-free where possible so the standalone test project can certify race semantics.

### Timeout/completion race

Use an atomic terminal decision conceptually equivalent to:

```text
if completion wins terminal ownership:
    process result under current generation
else if cancellation/abandon wins:
    do not process; perform provider-safe abandon cleanup
```

A result that physically completes after cancellation must not be ignored if ignoring it would strand provider-private claim state. The provider or ownership layer must have a deterministic cleanup path for that late result.

## 5. Passive prepared-delivery ownership

The current 12-second soft timeout may remain as UX/scheduling policy, but there must not be a second hard cutoff after which a future completion has nobody to resolve it.

Acceptable designs include:

- provider operation supports true cancellation and guarantees it cannot create a claim after cancellation returns; or
- an operation lease remains alive after the Unity coroutine stops and routes any late prepared delivery into a safe reject path; or
- provider preparation itself is refactored so claim acquisition occurs only after owner adoption.

Whatever shape is selected, prove:

- a timeout does not advance cursor/reward;
- pending movement stays retryable;
- a completion/cancel race resolves/abandons exactly once;
- next reconciliation is not permanently blocked by an old open claim;
- old-generation results cannot resolve a new-generation claim.

## 6. Android claim identity

The Android adapter currently delegates claim storage to `AndroidCounterReconciler` without exposing an identity token. Refactor the engine-free claim model so a prepared claim has a stable ID/token and resolution supplies the same token.

Recommended conceptual API (names may differ):

```text
TryClaimPending(out Claim { id, steps })
AcknowledgeClaim(claimId)
RestoreClaim(claimId)
```

Required semantics:

- opening claim A, restoring/acknowledging A succeeds once;
- repeated resolution of A is no-op;
- after A closes and B opens, resolving A cannot touch B;
- unknown/null IDs no-op;
- restored movement remains exactly-once retryable;
- reboot/anomaly behavior and persisted raw cursor semantics remain unchanged.

Do not use only a provider-side `deliveryId` check around an un-identified reconciler claim if that leaves the engine-free state machine incapable of proving the invariant.

## 7. Active-session adoption and completion

### Start

Provider start success is provisional until the domain accepts the Expedition. If `Activity.BeginExpedition` rejects after provider start, explicitly stop/abort the provider session and return to an idle provider state. No active native session may leak.

### Poll

Polling is observational. Cancellation/destruction must not mutate reward state. Late samples from an old runtime generation are discarded harmlessly.

### Stop/completion

All completion paths — normal `ExpeditionController`, debug completion, vehicle-like debug fixture, null result, stop fault/cancel — must converge on the sanctioned coordinator protocol.

The debug vehicle fixture may still construct trust/suspicion facts, but should hand a result or failure into the shared transaction boundary rather than call `ProcessSessionResult`, `CommitChanges`, and `ResolveSessionCompletion` directly.

If a stop operation fails and the provider can still hold base movement, the no-result coordinator path must durably close/repair the canonical marker and provider cleanup must preserve movement retryability.

## 8. GameHost provider release ordering

Before any path destroys the canonical service graph, release provider-owned resources first while enough references still exist to perform safe cleanup.

Audit and enforce ordering at least for:

- fatal persistence transition / `EnterBlockedState`;
- `RetryLoadFromDisk` success and runtime reconstruction;
- `StartOverWithFreshProfile`;
- `OnDestroy` / application quit path;
- any future service-graph rebuild helper.

Avoid a design where `Provider = null` is the teardown mechanism.

A provider teardown failure should be logged and contained; it must not cause destructive save behavior or fabricate reward.

## 9. Permission operation ownership

`MotionPermissionCoordinator` remains the domain-facing state machine, but its work must be cancelable/invalidatable by the UI/runtime owner.

Requirements:

- `UiComposer` uses a named state-change handler and detaches it.
- destruction cancels/invalidates refresh/request observation.
- late completion cannot call into destroyed UI or a newer coordinator generation.
- the OS permission itself may continue outside app control, but the discarded runtime must not retain callback ownership.
- denial remains a normal non-breaking state.

Do not broaden requested permissions.

## 10. Durability-gated presentation

### Expedition results

Treat processed session facts and durable reward presentation separately.

On `Committed`, show the positive reward summary based on the committed result.

On `RevertedToLastKnownGood`, clear success-only reward copy. Show only truthful failure/retryability copy. Base movement may be safe/retryable, but the rolled-back Vitality must not be displayed as earned.

On `FatalPersistenceLoss`, show recovery-state copy only; no positive reward summary.

If a duplicate-durable path is surfaced for active sessions in the future, it may show a non-duplicating “already recorded” result only if durable state proves that reward was previously committed.

### Start feedback

Do not play an Expedition-start success cue before provider start + domain adoption have actually succeeded. Failure feedback must not sound like success.

### Audio settings rollback

If volume/settings are applied optimistically before save and commit reverts, reapply actual canonical settings after `PersistenceReverted`/failed outcome so audible runtime state matches the profile.

## 11. Persistence rollback fidelity

Extend `ProfileStateCopier.CopyWorldState` to remove target-only nested `buildingStates` and `producerStates` keys for every surviving region after source entries have been copied/reused.

Do not replace surviving region/building/producer instances unnecessarily; existing services/presenters can hold references. The algorithm should be “reuse surviving values, copy fields, then prune stale keys.”

Consider other serializer-visible nested dictionaries/sets during implementation audit; if equivalent target-only retention exists, fix it in the same campaign and extend the fidelity test.

## 12. Dedup canonicalization

`CreditedActivityKeys.Rebuild()` should normalize serialized `entries` before constructing membership.

Required canonical policy:

1. remove null/empty keys;
2. collapse duplicates while preserving **most-recent occurrence order** (because capacity semantics are newest-N);
3. trim oldest unique entries until capacity;
4. rebuild `_set` exactly from the final list.

Example:

```text
capacity 3
input:  A, B, A, C, D
unique-most-recent order: B, A, C, D
bounded result: A, C, D
membership: {A, C, D}
```

The executor may implement this in one or two passes but must prove deterministic behavior.

## 13. Testing architecture

The standalone project currently excludes Unity `App`, `World`, and native platform adapters. M8.4 solved this for transaction ordering by extracting an engine-free coordinator. Follow the same principle where feasible:

- claim identity state machine belongs in engine-free Activity code;
- terminal operation ownership/race primitive belongs engine-free if introduced;
- provider-lifecycle protocol should be testable against deterministic fake providers;
- persistence/dedup fixes stay in standalone-covered code.

Do **not** copy Unity application logic into tests. Extract the smallest protocol surface and make Unity classes thin adapters.

PlayMode tests should still be added/extended for actual `GameHost` teardown/recomposition and presentation behavior when useful, but if the editor cannot run, mark execution UNVERIFIED while keeping source/static coverage.

## 14. Required ADR

Create ADR 0011 (or the next available number if another ADR landed) covering:

- provider instance lifetime;
- operation ownership/cancellation semantics;
- cancellation versus durable acknowledgment;
- timeout/completion race rule;
- old-generation callback invalidation;
- active-session start adoption and teardown;
- why this is preferred over longer timeouts/background orphan tasks.

Update earlier docs rather than silently contradicting ADR 0009/0010. ADR 0011 should extend, not replace, their two-phase durability rules.

## 15. No schema change by default

The intended fix does not require persisted model expansion. Dedup normalization is load-time repair of an existing field. Claim IDs can be transient/provider-side because process restart reconstruction already derives from the persisted cursor/native counter.

If the implementation concludes a persisted field is necessary, stop treating that as incidental: perform the full schema/migration/copier/test work required by `DATA_MODEL.md`.

## 16. Completion architecture

At completion, the repository should have this effective layering:

```text
Unity owner/controller
    -> owned/cancelable provider operation
    -> provider facts / prepared delivery
    -> engine-free activity transaction coordinator
    -> persistence outcome
    -> identity-bound provider resolution
    -> durability-gated presentation

GameHost lifetime
    -> cancel/invalidate owned operations
    -> idempotently shutdown provider/native work
    -> only then replace/drop canonical service graph
```

That is the M8.5 definition of runtime ownership.
