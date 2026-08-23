# ADR 0005 — Motion permission lifecycle and exactly-once activity partitioning

## Status

Accepted

## Context

The first implementation of the activity pipeline had three classes of defects found by
the bring-up/hardening audit (campaign S4-S8):

1. No permission-request flow existed. Android registered `TYPE_STEP_COUNTER` before
   `ACTIVITY_RECOGNITION` was granted (observing zeroed values), could not distinguish
   *denied* from *never asked*, and a fresh iOS install could never reach the API call
   that surfaces the Motion & Fitness prompt.
2. Passive reconciliation and active Expeditions shared the same sensor stream without a
   partition policy: passive polling drained the counter underneath a running session,
   double-crediting physical steps and corrupting session baselines.
3. Re-delivered Expedition results credited again, and the credited-interval store was
   never serialized, so every process restart silently reset dedup state.

## Decision

**Permission lifecycle**

- `IActivityProvider.RequestMotionPermissionAsync()` is the single contextual request
  path; UI invokes it only after explicit user intent. Prompts fire only when the state
  is effectively NotDetermined; repeat calls are retry paths; denial is a normal outcome.
- The engine-free `MotionPermissionCoordinator` sequences requests, prevents stacked
  prompts, and notifies UI; the platform remains the only authority for state.
- Android refines Denied-vs-NotDetermined with the rationale hint plus a process-side
  completed-request flag (Android 11+ stops showing rationale after repeated denials).
- Counter monitoring starts lazily once access is granted.
- iOS triggers its prompt with one benign asynchronous Core Motion query while
  NotDetermined, then polls status to resolution.

**Exactly-once partitioning**

- While an Expedition is active it owns the movement window: providers return no passive
  snapshot, and the domain ignores any snapshot delivered anyway. Android folds session
  deltas separately and restores pre-session residue to the passive stream on completion;
  on credit the sync cursor jumps past the session end so later passive windows cannot
  re-read those steps through historical queries.
- Durable `creditedSessionIds` join the existing interval dedup: a re-delivered result
  pays nothing again.

**Dedup persistence shape**

- `CreditedActivityKeys.entries` is a plain serialized `List<string>` field plus an
  explicit post-load `Rebuild()` (called from `SaveValidator`). A property-based design
  was rejected after Newtonsoft populated collection getters by reuse and never called
  setters - dedup state was silently lost on every restart. Schema version stays 1:
  additive fields with safe defaults are non-breaking under DATA_MODEL.md section 21.

**Blocking removal**

- All provider I/O is observed from Unity coroutines; no `.Result`/`.Wait()` remains on
  gameplay paths. The iOS bridge delivers historical queries through a marshalled
  callback (`WG_QueryPedometerAsync` + request ids); timed-out requests drop late native
  answers as stale, and failed queries leave the durable cursor untouched.

## Consequences

- Every rule above is domain-tested without hardware (`PermissionFlowTests`,
  `AndroidCounterReconciliationTests`, `IosHistoryPlanningTests`, extended
  `ActivityServiceTests`/`SaveLoadTests`); native shells stay thin.
- Physical-device gates (dialog UX, real sensor behavior) remain explicitly UNVERIFIED
  until hardware passes run; see `docs/IMPLEMENTATION_STATUS.md`.
- Any future provider must implement the same suppression contract or be excluded from
  passive reconciliation during sessions by design.
