# ADR 0011 — Runtime Ownership: Provider Lifetime & Async Operation Ownership

## Status

Accepted

## Context

ADR 0009 made provider movement delivery two-phase, and ADR 0010 centralized the
application transaction ordering in an engine-free coordinator with a certified
rollback-marker repair. A whole-repository audit of `main@616924fc` (M8.5 planning)
found that the remaining integrity gaps were not about data ownership but **operation
and instance ownership**:

1. **Android claim identity (H1):** `ResolvePreparedDelivery` checked only that *some*
   claim was open. A stale resolution for delivery A could acknowledge or restore a
   newer claim B because the engine-free reconciler claim had no identity token.
2. **Ownerless late passive task (H2):** after the ticker's 12 s deadline plus a 30 s
   hard-drain ceiling, a still-incomplete `PreparePassiveDeliveryAsync` had no owner;
   its eventual completion could strand an open provider claim until process restart.
3. **No provider teardown contract (H3):** `GameHost` dropped/rebuilt `Provider`
   references without releasing native monitoring or live pedometer state; a
   same-process replacement could inherit duplicate listeners or a leaked iOS
   `AlreadyRunning` condition.
4. **Unbounded Expedition tasks (H4):** start/poll/stop observation had no cancellation,
   generation guard, or destruction invalidation.
5. **Start adoption leak (H5):** provider start success followed by domain
   `BeginExpedition` rejection left the provider session running.
6. **Second transaction path (H6):** `UiComposer.VehicleSessionRoutine` re-implemented
   stop → trust → process → commit → resolve manually, and its fault path used an
   uncommitted abandonment that bypassed the M8.4 marker repair.
7. **Presentation truth (H7/M1/M2):** rolled-back rewards stayed visible as earned,
   permission UI leaked anonymous subscriptions, optimistic audio diverged from reverted
   canonical settings.
8. **Rollback fidelity (H8/H9):** `ProfileStateCopier` kept target-only nested
   building/producer keys inside surviving regions, and duplicate serialized dedup keys
   could make compaction "forget" a surviving credited key.

Cancellation must never imply durability, and timeout is scheduling policy — never a
durability boundary.

## Decision

### 1. Provider instances have explicit idempotent lifetime

`IActivityProvider` gains `void Shutdown()`:

- idempotent; repeated calls are harmless no-ops;
- after shutdown, operations refuse to start new work with benign fail-closed results
  (no preparation staging, no session start, no fabricated completion result);
- native passive monitoring stops (Android `stopMonitoring` bridge call; iOS
  `WG_StopPedometerUpdates`);
- provider-private claim/completion state is RESTORED to retryable form rather than
  consumed — teardown never acknowledges uncommitted movement as durable;
- restart reconstruction from the durable cursor plus native absolute sources stays intact.

Composition-root ordering (`GameHost`): every path that drops or rebuilds the service
graph calls `ShutdownProvider()` FIRST, while the provider can still release its own
native state — fatal blocked transition, retry-load reconstruction, start-over, and host
destruction (after the final autosave decision). `"Provider = null"` is never the
teardown mechanism. Teardown failures are logged and contained; they never cause
destructive save behavior.

### 2. Every async operation has exactly one terminal owner

An engine-free `OperationLease` admits exactly one terminal transition per operation:

- `TryAdopt()` — the owning coroutine processes the completed value once; or
- `TryAbandon()` — a timeout/cancellation/destruction path transfers terminal ownership
  to a deterministic cleanup continuation registered by `ProviderOperations`.

Never both owners process and abandon one operation; never neither. The cleanup
continuations touch only thread-safe provider instances — never canonical profile state,
never destroyed Unity objects — so an old generation completing after recomposition
cannot mutate the new runtime.

Timeout/completion race rule: whichever side wins the lease CAS owns the outcome. If
completion wins, the observer processes normally. If abandonment wins, any future
result converges provider state safely:

- abandoned passive preparations are REJECTED unprocessed (`durable=false`) so staged
  movement returns to retryable pending and cursors stay untouched;
- abandoned session stops resolve their result non-durably, returning held base
  movement to the passive stream exactly once;
- observational operations (poll/capability/permission) drop late results while still
  observing faults.

This REPLACES the M8.4 30-second hard-drain ceiling. There is no cutoff after which a
future completion becomes ownerless: the cleanup owner exists for the lifetime of the
task, so no provider claim can be stranded regardless of completion timing. The
12-second reconcile deadline remains pure scheduling policy.

### 3. Start adoption is explicit

A successful `StartSessionAsync` is provisional until `ActivityService.BeginExpedition`
accepts the session. On rejection the application aborts via `ActiveSessionAbort.Abort`:
the provider session is stopped and its base movement resolves non-durably back into the
passive stream — no leak, no reward, no consumption.

### 4. Claim resolution is identity-bound

`AndroidCounterReconciler` claims carry a stable ID (`OpenClaimId`);
`ClaimPending()` opens at most one identified claim and
`AcknowledgeClaim(id)` / `RestoreClaim(id)` resolve ONLY the named open claim. Stale,
repeated, unknown, and null ids are no-ops returning false. The Android adapter binds
`PreparedActivityDelivery.deliveryId` to that token, so a stale durable resolution for
delivery A cannot drop or restore newer claim B. Failed-commit retry behavior is
unchanged: rejection returns exactly the claimed movement once.

### 5. One transaction protocol owns all completions

Normal Expeditions, ticker debug sessions, and the debug vehicle fixture all delegate
to `ActivityTransactionCoordinator.CompleteExpedition` / `DeliverPreparedPassive`.
Debug conveniences may inject trust/suspicion facts (the vehicle fixture passes
location-evidence evidence), but they never re-implement process → commit → resolve →
repair. Repository-wide search confirms no unsanctioned completion sequence remains.

### 6. Canonical truth gates presentation

`ExpeditionResultPresentation` derives reward copy and completion status engine-free:
positive `+steps → +Vitality` copy exists only for proven committed outcomes; reverted
outcomes show truthful unsaved/retryability copy; fatal loss shows recovery copy only.
The Expedition-start success cue fires only after real provider start AND domain
adoption (`ExpeditionController.StartConfirmed`). Audio settings reapplied from canonical
profile values after `PersistenceReverted`. Permission UI uses a named detachable handler
plus bounded, owned request/refresh observations.

### 7. Rollback graph fidelity and dedup canonicalization

`ProfileStateCopier.CopyWorldState` prunes target-only nested `buildingStates` /
`producerStates` keys inside surviving regions after reusing surviving value instances,
so an in-place rollback serializes EXACTLY like the durable source (ADR 0007).
`CreditedActivityKeys.Rebuild()` canonicalizes load-time input — removes null/empty,
collapses duplicates onto most-recent occurrence, applies capacity to unique entries,
and rebuilds membership exactly from final entries — so compaction can never reopen a
surviving credited key.

## Consequences

- Stale Android resolutions can no longer mutate a newer claim; movement remains
  exactly-once retryable across failed commits, repeated resolutions, and restarts.
- Passive reconciliation can no longer orphan a provider claim under ANY completion
  timing; the previous documentation/code mismatch about the 30-second guarantee is
  resolved in favor of the stronger, now-real guarantee.
- Same-process runtime replacement releases native provider state before rebuilding, so
  replacement providers cannot inherit duplicate listeners or leaked live sessions.
- All M8.4 transaction invariants hold unchanged; ADR 0009/0010 two-phase durability
  rules are extended, not replaced. No persisted schema changed (claim ids are
  transient/provider-private; dedup canonicalization repairs existing fields).
- Headless certification now covers operation races, provider lifetime semantics,
  claim identity, rollback graph fidelity, and durability-gated presentation decisions;
  PlayMode timing/scene composition and device tiers remain UNVERIFIED until a licensed
  editor or hardware is genuinely available.
