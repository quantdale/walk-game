# Execution Prompt — M8.3 Movement Delivery Durability & Activity Write Discipline

**Status:** ACTIVE  
**Planned-From:** `main@15384b6bbf106b3a910c9b5a4152bcb562557fc9`  
**Target branch:** `main`  
**Campaign type:** non-hardware hardening / movement-state integrity  
**Priority:** High — exactly-once movement correctness must also avoid preventable movement loss on persistence failure

## Mission

Execute one coherent hardening campaign that closes the remaining activity-to-persistence transaction gap left deliberately deferred by M8.2.

The product already protects against duplicate movement credit aggressively, and M8.1 makes profile mutations transactional at the save boundary. The remaining defect class is on the **provider side**: Android and the debug provider can consume/drain movement before `GameHost.CommitChanges()` proves that the corresponding reward, dedup state, and cursor are durable. If the save then fails and the profile is rolled back, the in-memory provider can remain advanced and the same movement may no longer be available to retry in that process. M8.2 documented this as direction-safe (drop, never double), but it is still player-progress loss.

This campaign must make movement delivery participate coherently in the durability outcome while preserving the stronger invariant:

> One piece of real movement is credited at most once, and a transient persistence failure must not make already-observed base movement permanently disappear when it can still be recovered safely.

Also remove the documented `ActivityTicker` cadence write when the passive pipeline produced no durable profile mutation. Do not turn this into generic dirty tracking or unrelated performance work.

## Absolute repository boundary

This repository is **`quantdale/walk-game`**. It is NOT `quantdale/simple-walk-game`.

Before any mutation, run the repository identity guard and STOP on mismatch:

```bash
sh scripts/assert-repo-identity.sh
# or
./scripts/Assert-RepoIdentity.ps1
```

Never transfer prompts, SHAs, status, source, or assumptions from the sibling repository.

## Confirmed planner findings — starting evidence, not the whole audit

The planner reconciled current `main`, the completed M8.2 prompt/status record, recent commits, current branch/PR state, the activity provider abstraction, passive ticker path, Android/iOS/debug providers, `ActivityService`, `GameHost.CommitChanges()`, and the existing standalone test harness.

1. `.agent/EXECUTION_PROMPT.md` is COMPLETE for M8.2; there is no ACTIVE campaign to resume.
2. At planning time there are no open PRs and `main@15384b6b` is the authoritative line.
3. M8.2 explicitly deferred two related items:
   - Android movement already drained before a failed profile commit is currently dropped rather than doubled.
   - `ActivityTicker` can issue a no-op commit when passive processing produced no durable mutation.
4. `IActivityProvider` exposes `ReadSnapshotAsync(ActivityCursor)` and session start/poll/stop, but there is no provider delivery acknowledgment/rejection phase tied to application durability.
5. `ActivityTicker.ReconcileRoutine()` obtains a snapshot, mutates reward/dedup/cursor state through `ActivityService.ProcessPassiveSnapshot()`, then calls `host.CommitChanges()`. The provider read has already completed before the save result is known.
6. `AndroidStepSensorProvider.ReadSnapshotAsync()` folds the raw cumulative counter, calls `AndroidCounterReconciler.DrainPending()`, mutates the profile-backed Android raw-counter cursor, and returns the snapshot before the enclosing `CommitChanges()` resolves. A profile rollback does not automatically rewind the provider's private reconciler state.
7. `DebugActivityProvider.ReadSnapshotAsync()` similarly zeros its passive cumulative counter when read, before durability is known.
8. `IosCoreMotionProvider` is naturally more retryable because it performs historical queries from the persisted successful-sync cursor; a failed profile commit leaves that durable cursor behind so the same time window can be queried again. It still must conform to the final common provider contract without regressing historical dedup behavior.
9. Android Expedition completion also partitions/drains the folded counter and persists provider cursor facts before the application commit is known. Therefore this is not only a passive-poll problem; active-session completion must be audited under the same durability model.
10. `ActivityService.ProcessPassiveSnapshot()` returns accepted steps, not an explicit mutation outcome. `0` can mean several different things (suppression, duplicate, zero-step delivery, cursor-only/dedup mutation, or true no-op), so the caller cannot safely decide whether a durable write is required from that value alone.
11. `TECHNICAL_ARCHITECTURE.md` requires `VitalityLedger` credit and activity cursor persistence to be atomic and requires persistence failure to roll back/reload application state. M8.1 now enforces the profile half of that contract; provider-side consumption remains outside it.
12. The standalone domain suite compiles engine-free `Core`, `Building`, `Gameplay`, `Activity`, `Persistence`, `Content`, and EditMode tests. Keep new transaction mechanics engine-free where practical so the critical semantics are certifiable without a Unity license or physical device.

Treat these as seed findings. Re-audit the entire affected system before editing and preserve any equivalent fix that landed after this prompt was planned.

## Required startup / repository reconciliation

Before implementation:

1. Prove repository identity with the guard above.
2. Read `AGENTS.md` and `.agent/PLANNER_HANDOFF.md`.
3. Read the project docs in repository-required order, with special attention to:
   - `docs/MASTER_PLAN.md`
   - `docs/ROADMAP.md`
   - `docs/TECHNICAL_ARCHITECTURE.md`
   - `docs/DATA_MODEL.md`
   - `docs/ACTIVITY_REWARD_SYSTEM.md`
   - `docs/MOBILE_ACTIVITY_INTEGRATION.md`
   - `docs/PRIVACY_SAFETY_ANTI_CHEAT.md`
   - `docs/TESTING_AND_PERFORMANCE.md`
   - `docs/IMPLEMENTATION_STATUS.md`
   - ADR 0005, ADR 0007, ADR 0008, and any newer relevant ADR.
4. Record branch, HEAD, upstream, worktree status, `origin/main`, and recent history. Fetch before relying on old state.
5. Acquire the repository writer lease before the first mutation. One writer = one branch = one worktree. Use a campaign branch such as `agent/walk-game/m8.3-<session-id>`; do not share a mutable checkout with another agent.
6. If `origin/main` advanced beyond `15384b6b`, inspect every intervening commit/diff and reconcile this prompt against the new implementation before changing code. Do not redo landed fixes.
7. Recheck open PRs/issues and any useful in-flight work.
8. Run all currently available baseline gates and record exact evidence. Do not copy old counts as if they were produced now.

## Workstream A — whole-system movement/durability audit

Build a complete map before choosing the final design. Inspect the whole repository impact, not only the files named below.

At minimum trace:

- `Assets/WalkGame/App/ActivityTicker.cs`
- `Assets/WalkGame/App/ExpeditionController.cs`
- `Assets/WalkGame/App/GameHost.cs`
- `Assets/WalkGame/Activity/IActivityProvider.cs`
- `Assets/WalkGame/Activity/ActivityService.cs`
- `Assets/WalkGame/Activity/AndroidCounterReconciler.cs`
- `Assets/WalkGame/Activity/DebugActivityProvider.cs`
- `Assets/WalkGame/Platform/Android/CSharp/AndroidStepSensorProvider.cs`
- `Assets/WalkGame/Platform/iOS/CSharp/IosCoreMotionProvider.cs`
- persistence coordinator/repository/profile copier/serializer/validator/migrations
- `ActivitySyncState`, dedup containers, active-session state, provider cursor fields
- all activity, interruption-recovery, save-integrity, permission, and runtime certification tests
- every UI/feedback path that reacts to passive/Expedition completion.

Search the entire repository for every use of:

`ReadSnapshotAsync`, `StartSessionAsync`, `StopSessionAsync`, `DrainPending`, `RestorePending`, `PersistCursor`, `lastSuccessfulSyncUtc`, `androidLastRawStepCounter`, `creditedIntervals`, `creditedSessionIds`, `activeSession`, `CommitChanges`, `Persist`, lifecycle autosave, and activity-related domain events.

Audit both **duplicate risk** and **loss risk** across:

- successful passive reconciliation;
- duplicate/overlapping passive delivery;
- provider read fault/timeout/cancellation;
- save failure with in-place profile rollback;
- fatal mid-session persistence loss;
- process death before and after a provider delivery is prepared;
- app pause/focus storms;
- Android raw-counter reboot/reset/anomaly rebaseline;
- Expedition start, completion, failure, abandonment, and interrupted-session boot recovery;
- persisted cursor vs provider-private runtime state divergence.

Fix any Critical/High correctness defect exposed by this audit and any Medium defect required to make the transaction model coherent. Do not expand into unrelated features.

## Workstream B — explicit movement-delivery durability contract

Establish one coherent provider/application protocol so movement is not irreversibly consumed before the durability outcome is known.

The exact type names are flexible. A prepared-delivery/receipt/lease model is one reasonable shape, but do not implement ceremony for its own sake.

Required semantics:

1. A provider may **prepare** a passive snapshot or completed session result, but preparation alone must not make recoverable movement permanently unavailable.
2. The application processes that delivery against the canonical profile and attempts the required durable commit.
3. Only after a successful durable outcome may the provider irreversibly acknowledge/drop provider-private pending state associated with that delivery.
4. If the profile commit is reverted or otherwise rejected, the provider must restore/re-expose the delivery, or reconstruct it safely from its durable cursor/native absolute source, so retry does not lose base movement.
5. Resolve each prepared delivery exactly once. Repeated acknowledgments/rejections must be idempotent or rejected safely; never duplicate credit.
6. A process crash before provider acknowledgment must still converge safely after restart from persisted profile cursor + native absolute/history facts. Never require an in-memory receipt to survive process death for correctness.
7. The contract must work for Android, iOS, and debug implementations without leaking native details into reward/UI code.
8. Keep asynchronous provider operations non-blocking. Do not introduce `.Result`/`.Wait()` on Unity gameplay paths.
9. Keep the transaction mechanics IL2CPP-safe and deterministic. Avoid runtime reflection/serialization tricks for critical movement state.
10. Preserve privacy boundaries: no new raw route/location persistence or sensitive diagnostics.

If the cleanest design requires evolving `IActivityProvider`, update all implementations and callers together rather than adding an Android-only side channel.

## Workstream C — Android passive-counter loss elimination

For the Android cumulative counter path, prove and enforce these invariants:

1. Reading the latest raw cumulative counter may advance the **observed baseline**, but pending rewardable movement is not irreversibly discarded until the associated profile transaction commits.
2. On commit rejection/rollback, the same pending base movement becomes available to retry exactly once in the running process.
3. After process death, the persisted raw-counter cursor and live absolute sensor value reconstruct the uncommitted delta without double credit.
4. Reboot/reset and anomaly rebaselining remain fail-closed and never convert rejected movement into a huge or negative reward.
5. No overlap between a prepared passive delivery and an active Expedition can cause the same raw delta to exist in both ownership domains.
6. Any provider-private cursor/baseline mutated during preparation is reconciled with the rolled-back profile state after rejection; do not leave a split brain between `_reconciler` and `ActivitySyncState`.

Prefer moving any reusable pending-delivery state machine into engine-free `Activity` code if that materially improves deterministic coverage. Do not move native Android calls into the domain layer.

## Workstream D — Debug and iOS provider conformance

### Debug provider

Make the debug provider exercise the same semantics as production rather than hiding transaction defects:

- passive `Read`/prepare must not permanently zero its fake movement before durable acknowledgment;
- rejected delivery becomes retryable;
- acknowledged delivery cannot replay;
- session completion obeys the same resolution rules where applicable.

Add deterministic tests here because the debug provider is engine-free and part of the standalone suite.

### iOS provider

Preserve the current historical-query safety model:

- failed application commit must leave the durable successful-sync cursor behind so the window is retryable;
- provider resolution must not accidentally skip a historical interval;
- duplicate historical query results remain suppressed by durable dedup/cursor state;
- seven-day query-window behavior remains intact.

Do not invent native device evidence. C# planning/contract behavior can be AUTOMATED; Core Motion bridge callbacks remain DEVICE/EDITOR-tier where appropriate.

## Workstream E — Expedition completion durability

Audit and harden active-session completion under the same transaction model.

Required behavior:

1. `StopSessionAsync()` / equivalent must not make the session's base movement unrecoverable merely because the subsequent profile save failed.
2. A same-process retry may replay the exact same stable session identity/result until it is durably resolved; the domain dedup store must still make duplicate delivery harmless.
3. If the process dies after the native session ends but before the result is durably committed, base movement must remain recoverable from the platform's absolute/history source where the platform supports it. Optional Expedition bonuses may only be claimed when evidence is actually recoverable; never synthesize them.
4. Rejected completion must not leave `activeSession` suppression stuck forever or cause passive and active paths to both claim the same interval.
5. Successful completion advances partition/cursor state exactly once.
6. UI/audio/haptic success remains durability-gated by the existing M8.2 feedback mechanism.

Do not weaken the existing stale-Expedition boot recovery fix.

## Workstream F — explicit activity mutation outcome / no-op write discipline

Remove the documented no-op activity commit without introducing broad dirty tracking.

`ActivityTicker` must be able to distinguish at least:

- no provider delivery;
- delivery suppressed with no canonical change;
- duplicate delivery already proven durable, no new canonical change;
- cursor/dedup-only canonical change requiring persistence;
- reward/progression mutation requiring persistence;
- provider delivery that may be acknowledged without another profile save because durable state already proves it consumed.

Prefer a structured domain result (for example `StateChanged`, accepted steps, and delivery disposition) over inferring state change from `acceptedSteps == 0`.

Required result:

- do not call `CommitChanges()` on the 30-second activity cadence when the profile graph did not change;
- never skip a commit when cursor, dedup, reward, milestone, or any other durable field did change;
- provider acknowledgment must remain correct in both cases.

Do not expand this into whole-game dirty tracking, save batching, or speculative performance optimization.

## Workstream G — deterministic regression suite

Add tests that fail on the pre-campaign behavior and prove the new contract at the lowest engine-free layer possible.

Mandatory scenarios include:

1. passive movement prepared -> profile commit succeeds -> provider acknowledges -> movement credits exactly once;
2. passive movement prepared -> profile commit fails/reverts -> provider rejects/restores -> retry succeeds -> the original movement credits exactly once total;
3. repeated resolve/ack/reject calls cannot create duplicate credit or negative pending state;
4. process-restart reconstruction from old persisted cursor + newer absolute Android counter recovers an uncommitted window once;
5. Android reboot/reset after a rejected prepared delivery does not create a huge/negative/double reward;
6. overlapping reads / in-flight protection cannot prepare two claims over one pending window;
7. debug provider failed commit does not lose the fake passive counter;
8. duplicate passive interval whose durable state already contains the dedup/cursor performs no needless profile write and does not replay provider movement;
9. null/no-movement read performs no profile write;
10. cursor-only/dedup-only mutation still persists when required;
11. Expedition result commit failure remains retryable or otherwise recovers the same base movement without duplicate credit;
12. duplicate Expedition result/session ID remains harmless after retry/restart;
13. stale interrupted Expedition recovery still restores passive earning exactly once;
14. iOS historical planning/retry/dedup behavior stays unchanged.

Extend existing `ActivityServiceTests`, `AndroidCounterReconciliationTests`, `InterruptedSessionRecoveryTests`, `IosHistoryPlanningTests`, and save-integrity tests where that is the clearest home. Add a focused new test fixture if necessary.

For Unity-only `ActivityTicker` integration behavior, add PlayMode coverage if useful, but keep evidence marked UNVERIFIED unless a licensed editor actually runs it.

## Workstream H — cross-cutting regression audit after the contract changes

Because provider semantics are cross-cutting, re-audit the whole repository after implementation for effects on:

- permission request/denial/unavailable behavior;
- passive polls on focus/resume and cadence;
- timeouts, task faults, cancellation, and in-flight guards;
- exactly-once dedup across passive + Expeditions + save/restart;
- milestone awards and durable feedback queue behavior;
- M8.1 profile rollback/reference preservation;
- persistence blocked/recovery mode;
- lifecycle autosave;
- debug tools;
- Android/iOS platform assembly guards;
- serializer/validator/migrations if provider transaction metadata becomes durable;
- documentation claims vs actual evidence;
- release privacy/logging hygiene.

Fix introduced/exposed Critical and High regressions before completion. Fix Medium correctness/state-integrity regressions that are necessary to make the campaign safe. Record genuinely blocked hardware/editor concerns honestly rather than inventing workarounds.

## Documentation / architecture requirements

This changes the platform/application transaction boundary, so document it.

At minimum:

1. Add a new ADR (expected next number: **ADR 0009**, unless the repository advanced) describing the movement-delivery acknowledgment/rejection contract, crash semantics, provider responsibilities, and tradeoffs.
2. Update `docs/TECHNICAL_ARCHITECTURE.md` activity pipeline / transaction sections.
3. Update `docs/ACTIVITY_REWARD_SYSTEM.md` exactly-once/no-loss semantics.
4. Update `docs/MOBILE_ACTIVITY_INTEGRATION.md` Android/iOS provider delivery lifecycle.
5. Update `docs/IMPLEMENTATION_STATUS.md` with exact new evidence and resolved/deferred follow-ups.
6. Update `docs/DATA_MODEL.md` only if durable schema/state actually changes; if it does, follow the migration rules exactly.
7. Correct any stale contradictory documentation found by the whole-repo audit.

Do not mark physical-device or Unity-runtime behavior as verified from standalone tests.

## Validation gates

Run every genuinely available gate and record exact results from this campaign:

1. `dotnet test verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj`
2. `scripts/verify-domain.ps1`
3. `scripts/verify-release-hygiene.ps1`
4. `scripts/verify-unity-static.ps1`
5. `pwsh scripts/Test-AgentGuards.ps1`
6. repository identity guard against the live checkout
7. `git diff --check`
8. targeted activity/save tests repeatedly where useful to expose flaky transaction behavior
9. Unity EditMode/PlayMode only if the pinned editor has a real valid license; otherwise UNVERIFIED with the reproducible commands recorded
10. Android/iOS device/runtime checks only if the required build modules/hardware actually exist; otherwise UNVERIFIED

Before integration/push, fetch and run the remote-advance guard. If `origin/main` advanced unexpectedly, STOP automatic integration and reconcile deliberately. Never force-push.

## Acceptance gates

The campaign is complete only when all of the following are true:

1. No Android/debug passive delivery is irreversibly consumed before its durability outcome is resolved.
2. A transient save failure followed by retry does not permanently lose already-observed base movement and does not double-credit it.
3. Android provider-private reconciler state and profile-backed cursor state cannot remain divergent after a rejected commit.
4. Process restart after an unresolved delivery converges from durable cursor + native absolute/history facts without duplication.
5. Expedition completion has an explicit failure/retry/recovery story; a failed save cannot silently eat its base movement while the UI claims success.
6. Passive + active ownership partition remains exactly-once across all tested failure paths.
7. iOS historical retries remain safe under the common provider contract.
8. `ActivityTicker` does not persist when the profile graph truly did not change, while every cursor/dedup/reward mutation still persists.
9. Debug provider mirrors the transactional semantics closely enough to catch regressions in the standalone suite.
10. All newly discovered Critical/High regressions in the affected whole-repo audit are fixed with regression coverage.
11. Documentation matches the implemented contract and evidence tier exactly.
12. All available gates pass; blocked editor/device tiers remain explicitly UNVERIFIED.
13. Final tree is clean, remote advancement has been reconciled safely, and changes are committed/pushed without force.

## Constraints / non-goals

- Do not build Region 2, cloud save, multiplayer, combat, live ops, Health Connect, HealthKit expansion, or other Phase 9 work.
- Do not require GPS for passive step earning.
- Do not trade exactly-once safety for "never lose a step". When evidence is ambiguous, fail closed rather than minting duplicate currency.
- Do not fabricate optional Expedition bonus evidence after a process crash.
- Do not add a backend to solve this local transaction problem.
- Do not make platform-native code compute game rewards.
- Do not add global dirty tracking or broad save batching in this campaign.
- Do not bypass Unity licensing, UAC/elevation, signing, or hardware requirements.
- Do not suppress failing tests or weaken existing persistence/identity guards.
- Do not force-push, delete remote refs, or overwrite concurrent work.

## Completion / reporting requirements

At the end:

1. Re-run all available validation gates after the final code/doc changes.
2. Inspect the complete diff, not only the last files touched.
3. Update `docs/IMPLEMENTATION_STATUS.md` with exact test counts/results, systems changed, resolved follow-ups, UNVERIFIED tiers, and any deliberate remaining limitation.
4. Flip this prompt to `**Status:** COMPLETE` and append an executor report containing:
   - start SHA and final SHA;
   - root cause(s);
   - final transaction design;
   - provider-by-provider behavior (Android/iOS/debug);
   - passive and Expedition failure semantics;
   - tests added/changed;
   - exact validation results;
   - editor/device UNVERIFIED evidence;
   - any deferred follow-up with rationale.
5. Use detailed commit messages; the final commit message should double as the session report.
6. Fetch, run the remote-advance guard, integrate safely to `main`, push without force, and release the writer lease on normal completion.

Do not stop after making the happy path pass. The point of M8.3 is to prove the failure/retry/crash semantics across the complete movement delivery pipeline.
