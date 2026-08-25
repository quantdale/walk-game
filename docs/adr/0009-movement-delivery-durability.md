# ADR 0009 — Movement Delivery Durability and Activity Write Discipline

## Status

Accepted

## Context

M8.1 (ADR 0007) made the profile half of the activity transaction atomic: a
failed `GameHost.CommitChanges()` reverts the live profile graph in place to
disk truth, so reward credit, dedup keys, and the activity cursor can never
diverge from what is durable. M8.2 documented a remaining, deliberately
deferred defect class on the **provider side**:

- `AndroidStepSensorProvider.ReadSnapshotAsync()` drained the folded counter
  (`DrainPending()`) and advanced its private baseline *before* the enclosing
  profile commit was known. If that commit failed, the rolled-back profile no
  longer contained the movement, and the provider's pending state no longer
  held it either: the same walking was permanently lost (drop-never-double).
- `DebugActivityProvider.ReadSnapshotAsync()` zeroed its fake cumulative
  counter at read time with identical loss semantics.
- Android Expedition completion partitioned/drained the counter and persisted
  provider cursor facts before the application commit resolved; a failed save
  silently ate the session's base movement.
- `ActivityTicker.ReconcileRoutine()` committed on every non-null snapshot,
  even when reconciliation changed nothing canonical (suppressed reads, fully
  proven duplicates), producing documented no-op cadence writes.
- `ActivityService.ProcessPassiveSnapshot()` returned only accepted steps, so a
  caller could not distinguish "nothing happened" from "canonical state
  changed" and could not safely decide whether to persist or how to resolve a
  delivery.

The invariant to preserve and extend: **one piece of real movement is credited
at most once, and a transient persistence failure must not make already-
observed base movement permanently disappear when it can still be recovered
safely.** Exactly-once safety always wins over loss avoidance.

## Decision

### Prepared-delivery resolution contract

`IActivityProvider` replaces `ReadSnapshotAsync` with an explicit two-phase
surface:

1. `PreparePassiveDeliveryAsync(cursor)` stages the next passive window inside
   the provider and returns an opaque `PreparedActivityDelivery`. Preparation
   alone must not make recoverable movement permanently unavailable. Providers
   return null when nothing is deliverable or a previous delivery is still
   unresolved (in-flight protection; one open claim per provider).
2. The application processes the snapshot against the canonical profile and
   attempts the required durable commit, then resolves the delivery exactly
   once via `ResolvePreparedDelivery(delivery, durable)`:
   - `durable=true` acknowledges: the provider may irreversibly drop its staged
     state because the commit proved consumption.
   - `durable=false` rejects: the provider restores/re-exposes exactly this
     movement for one same-process retry.
3. Session completion follows the same pattern through
   `ResolveSessionCompletion(sessionId, durable)`; providers hold the
   session's base movement between `StopSessionAsync()` and resolution.
4. Resolutions of unknown, stale, repeated, or null deliveries are idempotent
   no-ops; they never duplicate credit or create negative pending state.
5. Process death never depends on in-memory receipts: restart reconstruction
   works from the persisted profile cursor plus the native absolute/history
   source. On Android the persisted raw-counter cursor only ever advances
   inside a committed profile, so a crash before commit replays the whole
   uncommitted window from the older durable cursor exactly once.

### Provider responsibilities

- **Android** (`AndroidCounterReconciler`, engine-free): `ClaimPending()`
  moves all pending steps into a single open claim; `AcknowledgeClaim()` drops
  it; `RestoreClaim()` returns it to pending. While a claim is open,
  `ClaimPending()` returns zero — overlapping reads cannot prepare two claims
  over one window. After rejection the runtime baseline intentionally stays
  ahead of the rolled-back persisted cursor: folds only credit increases from
  that baseline, and restart re-seeds conservatively from the persisted value,
  so both paths reconstruct the uncommitted window once. Reboot/anomaly
  rebaselining keeps previously restored pending steps intact and fail-closed.
  Expedition completion holds the session's base steps as a completion claim;
  rejection returns them to the passive stream (the session id was never
  durably marked, so no double credit is possible).
- **iOS**: preparation performs the usual historical query but consumes
  nothing private; both resolutions are no-ops. A failed commit rewinds the
  durable successful-sync cursor with the profile rollback, leaving the exact
  time window retryable through history queries; durable dedup/cursor state
  suppresses anything that did commit.
- **Debug**: mirrors production semantics — staging instead of zeroing,
  reject-restores the fake counter, session progress leaves the passive stream
  at stop (fixing a pre-existing latent double-credit where simulated session
  steps were delivered again passively after a durably credited session).

### Explicit mutation outcome / write discipline

`ActivityService.ProcessPassiveSnapshot` returns a structured
`PassiveReconciliationResult` (`NoDelivery`, `SuppressedBySession`,
`DuplicateDurable`, `DurableMutation`). The ticker commits only on
`DurableMutation` (reward/progression change, cursor/dedup-only repair), never
on suppressed or proven-duplicate passes, and resolves the provider delivery
correctly in every case: suppressed deliveries are rejected back (movement
stays retryable after the Expedition), proven duplicates are acknowledged
without another profile write.

## Consequences

- A transient save failure followed by retry credits observed base movement
  exactly once instead of losing it; ambiguous evidence still fails closed
  toward no double credit.
- The debug provider now exercises the transactional contract in the
  standalone suite, catching regressions without hardware or editor.
- No new durable schema fields were required; provider staging stays
  in-memory by design because crash recovery derives from durable cursors and
  native absolute facts (no serializer/migration impact).
- Unity-side scheduling glue remains engine-free-testable at the domain layer;
  ticker/Expedition coroutine behavior itself stays PlayMode-tier and remains
  UNVERIFIED until a licensed editor runs it.
