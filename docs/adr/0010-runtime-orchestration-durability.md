# ADR 0010 — Runtime Orchestration Durability & Headless Certification Boundary

## Status

Accepted

## Context

M8.3 (ADR 0009) established a correct provider-side transaction contract: prepare movement
without irreversible consumption, commit canonical state, then acknowledge or reject the
provider delivery exactly once. That contract was well-covered at the domain/provider level,
but the **application orchestration layer** that sequences provider calls, canonical
mutations, persistence rollback, lifecycle autosave, and player-facing Expedition state
remained outside the standalone 165-test headless gate.

`verification/WalkGame.Domain.Tests` compiles `Core`, `Building`, `Gameplay`, `Activity`,
`Persistence`, `Content`, and EditMode tests; it does not compile or execute the Unity `App`
orchestration classes (`ActivityTicker`, `ExpeditionController`, `GameHost` lifecycle glue).
PlayMode runtime certification exists but stays UNVERIFIED without a licensed editor. The most
important runtime sequencing — what happens after provider completion, domain mutation, commit
success/failure, rollback, and provider resolution — was therefore protected only by static
checks plus unexecuted PlayMode tests.

A planner audit of `main@c7d18f7` proved this was not merely a coverage gap. The concrete
failure-ordering defect reproduces deterministically:

1. An Expedition `activeSession` marker was previously persisted by lifecycle autosave
   (`GameHost.Persist()` on `OnApplicationPause`/`OnApplicationFocus` while an Expedition
   runs). This is recoverable state, not a bug: boot recovery (`ActivityService.RecoverInterruptedSession`)
   is the last-resort for process death.
2. The Expedition completes. `ExpeditionController.RunExpedition()` clears the marker in
   memory via `AbandonExpedition()`, processes the session result, then calls
   `CommitChanges()`.
3. The commit fails. `PersistenceCoordinator` reverts the live profile in place from disk
   via `ProfileStateCopier.CopyActivityState()`, which restores `activityState.activeSession`
   from the durable marker.
4. The provider is correctly rejected (`ResolveSessionCompletion(sessionId, false)`) and
   returns the session's base movement to the passive stream.
5. The resurrected canonical marker suppresses that passive delivery in the same process:
   `ActivityService.ProcessPassiveSnapshot()` returns `SuppressedBySession`, the delivery is
   rejected back to the provider, and every subsequent passive pass is also suppressed.
   The returned movement remains stranded until a later boot-time repair, even though the
   provider already made it retryable. The UI truthfully says "steps stay safe and will be
   credited once saving works again", but the same-process recovery never happens.

The existing M8.3 `MovementDeliveryDurabilityTests` did not reproduce the real controller
ordering. One failure test abandoned before rejection without rollback; another performed
rollback then explicitly called `AbandonExpedition()` **after** rollback before retry — the
runtime controller did not. Two additional defects of the same class were found during the
M8.4 whole-repo audit:

- **Fatal-loss NRE:** Both `ExpeditionController` (post-`CommitChanges` `ResolveSessionCompletion`)
  and `ActivityTicker.ReconcileRoutine` (post-`CommitChanges` `ResolvePreparedDelivery`) dereference
  `host.Provider`/`host.Activity` after a commit that may have triggered
  `GameHost.EnterBlockedState()` (which nulls `Profile`, `Activity`, `Provider`, etc.). A fatal
  persistence loss during completion crashed with `NullReferenceException` instead of converging
  to the fail-closed blocked recovery mode (ADR 0007).

- **Ticker timeout stranding:** `ActivityTicker.ReconcileRoutine()` observes
  `PreparePassiveDeliveryAsync()` with a 12-second deadline. On timeout it exits without a
  delivery value, but the underlying `Task` is not canceled. A provider that completes late
  (e.g. an Android claim already staged as an open `ClaimPending`) now has no owner to
  resolve it. While a claim is open, `ClaimPending()` returns zero, so all future
  preparations return `null` and passive earning is dead until restart. Android/debug
  currently complete synchronously, so the defect is latent; the application contract must
  not depend on that accident. The same concern applies to task faults that might leave
  claims open.

- **Stop-fault abandonment:** The `StopSessionAsync` fault/cancel/null branch cleared the
  marker in memory via `AbandonExpedition()` but never committed it and never resolved a
  provider completion. If a later passive commit failed, rollback could resurrect that stale
  marker (same defect class) and strand the next passive window.

The campaign's goal is to make the **real application transaction protocol** as rigorous and
executable as the M8.3 domain/provider contract, so a green headless suite means the movement
durability sequence used by the game is safe.

## Decision

### 1. Engine-free transaction coordinator — the headless certification boundary

An engine-free coordinator in `WalkGame.Activity` owns the ordering decisions that determine
**what happens after provider completion, domain mutation, commit outcome, rollback, and
provider resolution**:

- `ActivityTransactionCoordinator.CompleteExpedition(activity, provider, result, commit, growthEligible)`
- `ActivityTransactionCoordinator.DeliverPreparedPassive(activity, provider, delivery, commit)`
- `ActivityTransactionCoordinator.RejectAbandonedPreparation(provider, delivery)` (late/timeout drain)

`ActivityService`, `IActivityProvider`, and `PersistenceCommitOutcome` are the only
collaborators; the coordinator is stateless, has no `UnityEngine` dependency, and delegates
durability to a caller-supplied `Func<PersistenceCommitOutcome>`. `WalkGame.Activity`
now references `WalkGame.Persistence` so this boundary can see the three-way commit
outcome (`Committed | RevertedToLastKnownGood | FatalPersistenceLoss`) without duplicating
the enum. The verification project already globs both source trees, so the coordinator is
compiled by the headless gate; Unity MonoBehaviours become thin wiring over it. The change
is the smallest that achieves coverage: no fake Unity runtime, no DI framework, no broad
dirty-tracking batching.

`GameHost` exposes `CommitChangesWithOutcome()` returning `PersistenceCommitOutcome` with
identical event semantics to the existing `CommitChanges()` (`Committed` → `DurableCommitResolved(true)`,
`Reverted` → `PersistenceReverted` + `DurableCommitResolved(false)`, `Fatal` → `EnterBlockedState`
and no commit-resolved event). `CommitChanges()` becomes a wrapper (`outcome == Committed`).

### 2. Expedition completion / rollback repair sequencing (Workstream B)

**Normal result path:**

```
trust evaluation (coordinator)
  → activity.ProcessSessionResult(result, growthEligible)  // clears marker, credits reward, advances cursor past window
  → commit = CommitChangesWithOutcome()
  → Committed            → provider.ResolveSessionCompletion(sessionId, true)
  → RevertedToLastKnownGood → provider.ResolveSessionCompletion(sessionId, false)
                             → if activity.HasInterruptedSession → activity.RecoverInterruptedSession()
                               // repairs the marker resurrected by ProfileStateCopier from the durable autosaved
                               // activeSession; converges durably on next successful commit, reconstructible
                               // via boot recovery if the process dies first (Workstream E)
                               // report.repairedResurrectedMarker = true
  → FatalPersistenceLoss → do not touch provider (host is being torn down; provider discarded)
                          // movement in this window is unrecoverable by design (fail-closed, ADR 0007)
```

Invariants enforced:

- Provider/session reality and canonical `activeSession` cannot remain split-brain after rollback.
- A failed Expedition commit never leaves a stale marker that permanently suppresses the
  provider's rejected base movement in the same process.
- Repeated transient failures remain safe: `RestoreClaim`/`RestorePending` keep the exact
  rejected movement retryable; `RecoverInterruptedSession` is idempotent; no double credit
  is possible because the durable `creditedSessionIds` window was rolled back together with
  the marker.
- Duplicate completed session ids remain harmless (`TryMarkCredited` prevents re-credit).
- Optional Expedition bonuses that were never durably committed are not synthesized after
  failure/crash (constraints).

**No-result path** (`StopSessionAsync` fault/cancel/null):

The coordinator clears the marker via `AbandonExpedition()` **and durably closes it**
(`commit()`). On `Reverted`, the same resurrection repair runs. This closes the stop-fault
variant of the defect: an uncommitted abandonment can no longer be resurrected by a later
passive commit failure.

### 3. Passive delivery sequencing (Workstream C/D)

`DeliverPreparedPassive` processes the snapshot (`ActivityService.ProcessPassiveSnapshot`
returns `PassiveReconciliationDisposition`), then:

- `SuppressedBySession` / `NoDelivery` / `DuplicateDurable` → resolve exactly once without a
  commit (`Suppressed` → `durable=false` / reject; `DuplicateDurable` → `durable=true` / ack
  without a save — ADR 0009). No profile write.
- `DurableMutation` → `commit()`:
  - `Committed` → `ResolvePreparedDelivery(delivery, true)` (ack)
  - `Reverted` → `ResolvePreparedDelivery(delivery, false)` (reject) + **repair** any
    resurrected marker as above. This branch is unreachable during a genuinely live
    Expedition because suppressed deliveries never commit; it handles the
    expedition-fail→passive-fail resurrection window. The repair makes the next
    passive retry non-suppressed.
  - `Fatal` → do not resolve the delivery (provider discarded); movement in this
    `DurableMutation` window is correctly unrecoverable (fail-closed) — identical to
    the Expedition fatal semantics.

`GameHost.Current` / `host.Provider` / `host.Activity` are captured before `commit()` so
a fatal transition that nulls host fields cannot NRE on the subsequent provider line.
Both `ExpeditionController` and `ActivityTicker` were fixed for this.

### 4. Late-completion / timeout ownership (Workstream D)

`ActivityTicker.ReconcileRoutine()` is the only Unity-scheduled site that observes an
asynchronous provider task. The 12-second deadline remains the "failed query" boundary
(fail-closed, cursor untouched). After a deadline expiry the coroutine **continues observing
the same task on the main thread** up to a hard cap (30 additional seconds) instead of
abandoning it:

- If the task eventually faults/cancels → standard `HandleFault` path, no delivery staged,
  cursor untouched.
- If it completes with a `PreparedActivityDelivery` → the delivery is **rejected without
  ever being processed**: `RejectAbandonedPreparation(provider, delivery)` (`durable=false`)
  so the staged claim (Android `ClaimPending` / debug `_passiveClaim`) returns to pending
  and the same movement is retried on the next cycle. The sync cursor never advances for
  a timed-out window.

If the hard cap expires without completion, an error is logged and the claim may remain
open until process restart — the only remaining unbounded wait (a provider that never
completes is a provider bug; no arbitrary `Task` cancellation is injected). Overlapping
reconciliations remain guarded by `_reconcileInFlight`; focus-change storms drop triggers.
`ActivityProcessed` still fires once per pass so idle production/HUD stay live.
The contract no longer depends on the current synchronous completion of Android/debug;
iOS historical queries (which consume nothing private) remain safe.

### 5. Lifecycle autosave vs transactional mutation convergence (Workstream E)

- `GameHost.Persist()` remains the non-transactional lifecycle autosave path and is still
  gated on `!PersistenceBlocked` (ADR 0007). It is intentionally allowed to persist a
  recoverable `activeSession` marker while an Expedition runs; that marker is either
  repaired in-process by the coordinator after a failed completion, or cleared by
  `RecoverInterruptedSession()` at next boot.
- `OnDestroy` cannot persist a transient failed mutation: `PersistenceCoordinator` reverts
  the live graph in place before `Persist()` sees it; a post-rollback repair (`RecoverInterruptedSession`
  clearing the resurrected marker) will then be persisted by the next lifecycle `Persist()` or
  the next successful `CommitChanges()` — the repair converges durably, and process death
  before convergence is still reconstructible via boot recovery.
- `CommitChanges()` and lifecycle `Persist()` are both invoked on the Unity main thread
  (coroutine steps and `OnApplicationPause`/`OnApplicationFocus` are main-thread), so no
  concurrent interleaving exists; the assumption is now proven by structure and a code
  comment rather than intuition.
- Persistence-blocked runtime never writes preserved material (`Persist` and the new
  `CommitChangesWithOutcome` both fail closed).

### 6. Verification shape

`ApplicationOrchestrationTests` (engine-free, 17 scenarios) covers the 14 mandatory
headless scenarios (F1–F14) through the coordinator surface plus edge variants; the
existing `MovementDeliveryDurabilityTests` (14), `AndroidCounterReconciliationTests` (6),
`ActivityServiceTests` (+1), `InterruptedSessionRecoveryTests`, `SaveIntegrityApplicationTests`,
and `SaveLoadTests` (+1) remain green. The Unity `App` MonoBehaviours keep thin,
inspectable wiring; a compile-time linkage exists via the `WalkGame.Activity` →
`WalkGame.Persistence` assembly reference and the `CommitChangesWithOutcome` API — a drift
that bypasses the coordinator would be caught by the new tests or by the standing
`verify-unity-static` / `verify-domain` gates.

## Consequences

- The persisted-marker rollback resurrection defect is reproduced headlessly and repaired;
  same-process passive recovery now succeeds exactly once without requiring a restart, and
  repeated transient save failures before eventual success never double-credit.
- Fatal persistence loss during completion or passive reconciliation no longer NREs; it
  fails closed and the provider's held movement is correctly unrecoverable (the blocked
  recomposition replaces the UI).
- Late/timeout provider preparations cannot strand an open claim indefinitely; the general
  drain-and-reject rule holds for all providers.
- Stop fault/cancel/null paths durably close the canonical marker and repair on revert,
  closing the secondary resurrection window.
- No new durable schema fields were required; repairs are in-memory and converge on the
  next durable commit, with boot recovery as the crash-safe fallback.
- The standalone .NET project now compiles and executes the correctness-critical
  transaction decisions; PlayMode ticker/Expedition timing, scene composition, and
  provider JNI callbacks remain honestly UNVERIFIED without the corresponding hardware/editor.

## Amendment (M8.5 / ADR 0011)

Section 4's hard-drain ceiling was superseded by ADR 0011's terminal-ownership lease:
the "30 additional seconds" observation window and its "claim may remain open until
process restart" residual are replaced by a deterministic cleanup owner that survives
the observing coroutine and rejects any late-completing preparation whenever it arrives.
There is no longer any cutoff after which a future completion becomes ownerless. All
guarantees in this document remain; the drain-cap paragraph is historical record.
