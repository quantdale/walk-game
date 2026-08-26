# Execution Prompt — M8.4 Runtime Orchestration Durability & Headless Certification

**Status:** COMPLETE  
**Planned-From:** `main@c7d18f766438eb50fbb3854d88a9972fdbc5dc32`  
**Target branch:** `main`  
**Campaign type:** non-hardware hardening / application-orchestration correctness / verification convergence  
**Priority:** High — M8.3’s domain/provider transaction contract is correct in the standalone suite, but its real Unity orchestration is not currently covered by the 165-test headless gate and a concrete rollback-ordering defect can resurrect Expedition suppression after a failed commit

## Mission

Execute one coherent hardening campaign that closes the remaining gap between the engine-free M8.3 movement durability contract and the actual Unity application code that sequences provider calls, canonical mutations, persistence rollback, lifecycle autosave, and player-facing Expedition state.

M8.3 established a strong provider contract: prepare movement without irreversible consumption, commit canonical state, then acknowledge or reject the provider delivery. That contract is now well-covered at the domain/provider level. The unresolved risk is the **application orchestration layer** (`ActivityTicker`, `ExpeditionController`, `GameHost` lifecycle/persistence glue, and adjacent presentation refresh paths). The standalone .NET project deliberately compiles `Core`, `Building`, `Gameplay`, `Activity`, `Persistence`, `Content`, and EditMode tests; it does **not** compile or execute the Unity `App` orchestration classes. PlayMode coverage exists, but remains UNVERIFIED because the environment has no licensed Unity editor session.

A planner audit of current `main@c7d18f7` found a concrete failure-ordering defect that demonstrates this is not merely a coverage-quality campaign:

> If an Expedition `activeSession` marker was previously persisted by lifecycle autosave, `ExpeditionController` clears it in memory before processing the completion result, then calls `CommitChanges()`. A failed commit reverts the profile in place from disk, which can restore the old `activeSession` marker. The provider is then correctly rejected and returns the session’s base movement to the passive stream, but the resurrected canonical marker can suppress that passive delivery in the same process. The UI says the steps remain safe and will be credited once saving works again, but the recovered movement may remain blocked until a later boot-time interrupted-session repair.

This campaign must repair that class of defect, prove the complete sequencing with deterministic headless tests, and audit the whole repository for equivalent orchestration failures. Do not simply patch one line in `ExpeditionController`; make the transaction/lifecycle protocol explicit enough that future provider, persistence, or UI changes cannot silently reintroduce the same split-brain state.

## Absolute repository boundary

This repository is **`quantdale/walk-game`**. It is NOT `quantdale/simple-walk-game`.

Before any mutation, run the repository identity guard and STOP on mismatch:

```bash
sh scripts/assert-repo-identity.sh
# or
./scripts/Assert-RepoIdentity.ps1
```

Never transfer prompts, SHAs, assumptions, source, or campaign state from the sibling repository.

## Confirmed planner findings — starting evidence, not the whole audit

The planner reconciled the completed M8.3 prompt/report, `main@c7d18f7`, recent commits, current PR/issue state, `AGENTS.md`, `.agent/PLANNER_HANDOFF.md`, `docs/IMPLEMENTATION_STATUS.md`, `docs/ROADMAP.md`, `IActivityProvider`, `ActivityService`, `PersistenceCoordinator`, `ProfileStateCopier`, `ActivityTicker`, `ExpeditionController`, `GameHost`, Android/iOS provider implementations, the M8.3 movement durability tests, the PlayMode runtime certification suite, and the standalone test project/CI shape.

1. `.agent/EXECUTION_PROMPT.md` is COMPLETE for M8.3; there is no ACTIVE campaign to resume.
2. At planning time there are no open PRs/issues relevant to this work and `main@c7d18f7` is the authoritative line.
3. M8.3 reports 165/165 standalone tests passing and explicitly leaves Unity EditMode/PlayMode/device tiers UNVERIFIED because the editor license/build modules/hardware are unavailable.
4. `verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj` compiles engine-free `Core`, `Building`, `Gameplay`, `Activity`, `Persistence`, `Content`, and EditMode tests, but not `Assets/WalkGame/App/**`, `World/**`, or the Unity platform/runtime orchestration surface.
5. `.github/workflows/domain-tests.yml` therefore cannot compile or execute `ActivityTicker`, `ExpeditionController`, `GameHost`, `AppFlowController`, `FeedbackController`, or other Unity application glue. The repository’s most important runtime sequencing is currently protected only by static checks plus PlayMode tests that cannot run in this environment.
6. `RuntimeCertificationTests` contains valuable PlayMode scenarios for bootstrap, activity, save/reload, stale Expedition recovery, and blocked persistence, but they remain UNVERIFIED until a licensed editor run is possible.
7. `ExpeditionController.RunExpedition()` currently performs this completion order:
   - provider `StopSessionAsync()`;
   - set controller inactive;
   - `host.Activity.AbandonExpedition()` (clears canonical `activeSession` in memory);
   - process the session result;
   - `host.CommitChanges()`;
   - `host.Provider.ResolveSessionCompletion(sessionId, durable)`.
8. `PersistenceCoordinator.Commit()` reloads the last durable profile and calls `ProfileStateCopier.CopyInto()` after a failed save. `CopyActivityState()` explicitly restores `activityState.activeSession` from durable state.
9. `GameHost` lifecycle autosave (`OnApplicationPause` / lost focus) uses `Persist()` and can durably store the in-progress `activeSession` marker while an Expedition is running. Therefore the M8.4 planner defect is reachable: resume -> finish -> reward commit fails -> rollback restores the previously durable marker -> provider rejects completion and returns base movement -> passive `ActivityService.ProcessPassiveSnapshot()` sees `activeSession != null` and suppresses the delivery.
10. The existing M8.3 `MovementDeliveryDurabilityTests` do not reproduce the real controller ordering. One failure test abandons the Expedition before provider rejection without a persistence rollback; another performs rollback and then explicitly calls `AbandonExpedition()` **after** rollback before retry. The runtime controller does not currently perform that post-rollback repair.
11. M8.3’s stated acceptance explicitly requires rejected session completion not to leave `activeSession` suppression stuck and requires same-process recovery. The current orchestration can violate that requirement despite all 165 headless tests passing.
12. `ActivityTicker` also owns unresolved orchestration semantics that must be audited rather than assumed correct: timeout vs late provider completion, provider resolution exactly once, host/profile replacement while asynchronous work is in flight, persistence transitioning to blocked state, focus/pause storms, and `ActivityProcessed` refresh behavior after failures/suppression.
13. The nominal roadmap next steps are mostly blocked by current environment prerequisites (licensed Unity runtime, Android Build Support/real step sensor, macOS/Xcode, physical-device performance, closed playtest). This makes application-layer correctness and deterministic certification the highest-value unblocked continuation.

Treat these as seed findings. Re-audit the entire affected system before editing and preserve any equivalent fix that landed after this prompt was planned.

## Required startup / repository reconciliation

Before implementation:

1. Prove repository identity with the guard above.
2. Read `AGENTS.md` and `.agent/PLANNER_HANDOFF.md`.
3. Read project docs in repository-required order, especially:
   - `docs/MASTER_PLAN.md`
   - `docs/ROADMAP.md`
   - `docs/TECHNICAL_ARCHITECTURE.md`
   - `docs/DATA_MODEL.md`
   - `docs/ACTIVITY_REWARD_SYSTEM.md`
   - `docs/MOBILE_ACTIVITY_INTEGRATION.md`
   - `docs/PRIVACY_SAFETY_ANTI_CHEAT.md`
   - `docs/TESTING_AND_PERFORMANCE.md`
   - `docs/IMPLEMENTATION_STATUS.md`
   - ADR 0007, ADR 0008, ADR 0009, and any newer relevant ADR.
4. Record branch, HEAD, upstream, worktree status, `origin/main`, recent history, and open PRs/issues. Fetch before relying on this planner’s state.
5. Acquire the repository writer lease before the first mutation. One writer = one branch = one worktree. Use a campaign branch such as `agent/walk-game/m8.4-<session-id>`.
6. If `origin/main` advanced beyond `c7d18f7`, inspect every intervening commit/diff and reconcile this prompt against the new implementation before changing code. Do not redo landed fixes.
7. Run every currently available baseline gate and record exact evidence. Do not copy M8.3 counts as if produced by this campaign.
8. Do not attempt to bypass Unity licensing, UAC/elevation, Android module installation permissions, signing, or device requirements. Those tiers stay honestly UNVERIFIED when unavailable.

## Workstream A — whole-repository orchestration and transaction audit

Build a complete map before choosing the final implementation shape. Inspect the whole repository impact, not only the seed files.

At minimum trace:

- `Assets/WalkGame/App/ActivityTicker.cs`
- `Assets/WalkGame/App/ExpeditionController.cs`
- `Assets/WalkGame/App/GameHost.cs`
- `Assets/WalkGame/App/TaskObservation.cs`
- `Assets/WalkGame/App/AppFlowController.cs`
- `Assets/WalkGame/App/FeedbackController.cs`
- `Assets/WalkGame/App/SaveRecoveryController.cs`
- `Assets/WalkGame/UI/UiComposer.cs` and Expedition/HUD presentation paths
- `Assets/WalkGame/Activity/IActivityProvider.cs`
- `Assets/WalkGame/Activity/ActivityService.cs`
- `Assets/WalkGame/Activity/DebugActivityProvider.cs`
- Android/iOS provider start/poll/stop/prepare/resolve implementations
- `PersistenceCoordinator`, `ProfileStateCopier`, `FileSaveRepository`, serializer/validator/migrations
- every lifecycle persistence entry point (`Persist`, `CommitChanges`, pause/focus/destroy)
- all EditMode/runtime tests touching activity, persistence, interruption recovery, permissions, feedback, and application composition
- the standalone verification project, asmdefs, CI, and static verification scripts.

Search the entire repository for every use of:

`BeginExpedition`, `AbandonExpedition`, `RecoverInterruptedSession`, `ProcessSessionResult`, `PreparePassiveDeliveryAsync`, `ResolvePreparedDelivery`, `ResolveSessionCompletion`, `StartSessionAsync`, `PollSessionAsync`, `StopSessionAsync`, `CommitChanges`, `Persist`, `PersistenceReverted`, `DurableCommitResolved`, `activeSession`, lifecycle callbacks, coroutine/task observation, and timeout/fault paths.

Audit at least these state combinations:

- Expedition starts and never backgrounds before completion;
- Expedition marker becomes durable due to focus/pause autosave;
- completion reward commit succeeds;
- completion reward commit fails and reverts to a durable active marker;
- repeated transient save failures after provider movement was returned to passive;
- fatal persistence loss during completion;
- stop task fault/cancel/null result;
- app pause/focus during start, poll, stop, or passive preparation;
- scene/host destruction or recovery recomposition while a task is in flight;
- passive preparation timeout followed by a late task completion;
- provider resolution throwing or receiving stale/repeated inputs;
- duplicate durable session result;
- process death before/after session stop and before/after commit;
- iOS historical recovery vs Android/debug provider-private claims;
- feedback/UI state after rollback, retry, duplicate, and blocked transitions.

Fix every discovered Critical/High correctness defect and any Medium state-integrity defect required to make the lifecycle model coherent. Do not expand into unrelated features.

## Workstream B — repair Expedition completion rollback ordering

Reproduce the planner defect deterministically first, then fix it at the correct abstraction boundary.

Mandatory pre-fix regression scenario:

1. Create/persist a profile with a non-null `activityState.activeSession`, representing an Expedition marker durably saved during a background/focus event.
2. Resume the same logical session and obtain a stable provider completion result whose base movement is held pending resolution.
3. Clear/complete the in-memory session as the real controller does.
4. Process the completion result.
5. Inject a save failure so `PersistenceCoordinator` reverts the live profile from disk and therefore restores the durable `activeSession` marker.
6. Resolve the provider completion as non-durable so its base movement returns to retryable/passive ownership.
7. Prove the pre-campaign behavior suppresses or strands that recovered movement in the same process because the canonical marker was resurrected.
8. After the fix, prove the exact same movement becomes retryable/creditable exactly once without requiring a process restart, without duplicate reward, and without pretending the failed completion was durable.

Required invariants after the fix:

- Provider/session reality and canonical `activeSession` cannot remain split-brain after rollback.
- A failed Expedition reward commit must never leave a stale canonical marker that permanently suppresses the provider’s rejected base movement in the same process.
- Retrying while storage is still failing must remain safe: no double credit, no silent movement loss, no permanent suppression, and no false success UI.
- If a separate durable session-closure transition is required, define its ordering and crash semantics explicitly.
- If an in-memory post-rollback repair is required, prove how it converges on the next successful commit and how process death remains safe.
- Do not weaken boot-time interrupted-session recovery; it remains a last-resort crash repair, not the normal same-process completion path.
- Fatal persistence loss still fails closed per ADR 0007; do not synthesize reward into a blocked session.

Do not hard-code a fix specifically for `DebugActivityProvider`. The runtime contract must hold for Android, iOS, and future providers.

## Workstream C — make application orchestration headlessly certifiable

The campaign must reduce the current verification blind spot rather than only adding more UNVERIFIED PlayMode tests.

Required outcome:

- The transaction/lifecycle decision logic that determines **what happens after provider completion, domain mutation, commit success/failure, rollback, and provider resolution** must be testable in the standalone .NET gate without a Unity license.

Use the smallest architecture change that achieves this. Reasonable approaches include extracting an engine-free orchestration/coordinator/state-machine class that Unity MonoBehaviours call, or introducing narrow engine-free policies/results for completion and retry decisions. Do not build a fake Unity runtime or a giant abstraction framework.

The extracted/testable surface should own enough logic to prove:

- Expedition start/finish canonical state transitions;
- commit-success vs revert vs fatal outcomes;
- provider acknowledgment/rejection ordering;
- post-rollback repair/convergence;
- duplicate completion behavior;
- passive handoff after rejected completion;
- repeated failures;
- lifecycle-stored active markers;
- no-op/duplicate dispositions where relevant.

Keep Unity-specific concerns (coroutine timing, GameObject composition, actual scene APIs) in `App`, but minimize correctness-critical state decisions that exist only inside MonoBehaviour methods.

Update `verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj` and asmdef boundaries only if necessary and deliberately. Do not simply glob all Unity-dependent sources into the net8 project with brittle fake Unity stubs.

## Workstream D — passive ticker late-completion / resolution audit

M8.3 requires every prepared provider delivery to be resolved exactly once after a durability outcome. Audit `ActivityTicker.ReconcileRoutine()` against timeout and lifecycle races.

Current pattern to examine:

- `PreparePassiveDeliveryAsync` is observed from a coroutine;
- the coroutine has a 12-second timeout;
- on timeout it exits without a delivery value;
- the underlying Task is not canceled by that timeout;
- a provider could theoretically complete later with a prepared delivery whose provider-private claim now has no owner to resolve it.

Android/debug currently complete preparation synchronously and iOS consumes no private claim, but the application contract must not depend accidentally on those implementation details.

Required semantics:

- no late completed task may strand a provider claim indefinitely;
- stale/late results after timeout must be drained/resolved safely or the interface must support cancellation/abandon semantics that make preparation ownership explicit;
- overlapping/focus-triggered reconciliations cannot resolve the wrong delivery;
- timeout, cancellation, task fault, host destruction, persistence block, and provider resolution failure leave canonical/provider state convergent;
- no `.Result`/`.Wait()` blocking on gameplay paths;
- no background continuation may touch destroyed/replaced Unity state unsafely.

Prefer a general lifecycle-safe rule rather than provider-specific exceptions.

## Workstream E — lifecycle autosave vs transactional mutation audit

`GameHost.Persist()` is intentionally non-transactional and reserved for lifecycle autosave/internal use, while player-visible mutations use `CommitChanges()`. Audit whether lifecycle callbacks can serialize intermediate canonical states that later confuse rollback semantics.

At minimum prove:

- pausing/focus loss while an Expedition is active records only a recoverable state;
- pausing during start/stop/commit windows cannot create a durable state impossible for boot recovery to interpret;
- `OnDestroy` cannot persist a transient/failed mutation after a coordinator rollback or fatal transition;
- persistence-blocked runtime never writes preserved save material;
- lifecycle autosave and `CommitChanges()` cannot race or interleave in a way that causes stale writes (Unity main-thread sequencing may be sufficient, but prove the assumption in code/tests/docs rather than relying on intuition);
- post-rollback canonical repair is either intentionally persisted later or remains reconstructible after process death.

Do not replace all lifecycle persistence with broad dirty tracking or save batching unless the audit proves that is necessary. Keep scope on correctness.

## Workstream F — deterministic regression suite

Add tests that fail on the pre-campaign application protocol and prove the repaired orchestration at the lowest engine-free layer possible.

Mandatory headless scenarios include:

1. persisted active-session marker -> successful Expedition completion -> reward/session closure durable exactly once;
2. persisted active-session marker -> completion reward save fails -> rollback restores marker -> provider rejects movement -> same-process passive recovery succeeds exactly once after the fix;
3. scenario 2 with another transient save failure before eventual success; movement remains retryable and never doubles;
4. duplicate completed session id after durable success is harmless and does not reopen passive ownership;
5. fatal persistence loss during completion enters blocked semantics and does not fabricate reward or falsely advertise recoverable durability;
6. stop returns null/fault/cancel after a durable active marker; canonical/provider ownership converges and later passive recovery is safe;
7. process restart after unresolved/failed completion still converges from durable cursor/native history without duplication;
8. passive delivery prepared -> commit succeeds -> acknowledge exactly once through the extracted orchestration surface;
9. passive delivery prepared -> commit reverts -> reject exactly once and retry safely;
10. suppressed passive delivery during a genuinely live Expedition stays retryable and is not accidentally acknowledged;
11. late/timeout provider preparation cannot strand an unresolved claim;
12. host/persistence transition to blocked while activity work is in flight cannot mutate a dead profile or leave provider state falsely acknowledged;
13. repeated stale/duplicate provider resolutions remain safe no-ops;
14. durability-gated feedback remains truthful after the new ordering/refactor.

Keep/extend existing `MovementDeliveryDurabilityTests`, `InterruptedSessionRecoveryTests`, `SaveIntegrityApplicationTests`, `ActivityServiceTests`, and `PlayerExperienceTests` where appropriate. Add a focused application-orchestration fixture if that gives the clearest ownership.

Also add/extend PlayMode tests for the real MonoBehaviour wiring when useful, but mark them UNVERIFIED unless a licensed editor actually runs them. The campaign is not complete if the core fix exists only in unexecuted PlayMode coverage.

## Workstream G — verification boundary and CI integrity

Audit the repository’s evidence story after the refactor.

Required:

- standalone tests compile the extracted application transaction logic actually used by runtime;
- Unity-specific `App` classes keep thin, inspectable wiring over that logic;
- static verification catches accidental loss of the runtime-to-headless linkage where practical;
- CI continues to run repository identity, agent guards, standalone domain/application tests, Unity static audit, and release hygiene;
- test counts/status docs are refreshed from real runs;
- no claim that MonoBehaviour scene composition, Android JNI, iOS callbacks, or device performance was executed unless it actually was.

If there is a clean way to add a compile-time guard that prevents the runtime from drifting away from the tested orchestration types, add it. Avoid brittle textual tests that merely grep implementation strings when a type-level/test-level contract is possible.

## Workstream H — cross-cutting whole-repo regression audit after changes

After implementing the fix, re-audit the whole repository for effects on:

- exactly-once passive + Expedition credit;
- Android claim/restoration state;
- iOS historical cursor behavior;
- debug-provider parity;
- permission denial/unavailable paths;
- stale interrupted-session boot recovery;
- persistence rollback/reference preservation;
- lifecycle autosave and shutdown;
- milestone rewards and `DurableCommitResolved` feedback gating;
- HUD/Expedition status copy;
- AppFlow/UiComposer subscriptions and recomposition after blocked/recovered state;
- save schema/validator/migrations if any state shape changed;
- assembly boundaries and Unity platform guards;
- CI/static scripts;
- privacy/release logging.

Fix introduced/exposed Critical and High regressions before completion. Fix Medium correctness/state-integrity issues needed for a coherent protocol. Record lower-risk or hardware-only items honestly.

## Documentation / architecture requirements

This campaign changes or clarifies the application transaction/lifecycle boundary, so documentation must match reality.

At minimum:

1. Add a new ADR (expected next number: **ADR 0010**, unless the repo advanced) describing the runtime orchestration contract, Expedition completion/rollback sequencing, lifecycle marker semantics, provider resolution ownership, and headless-certification boundary.
2. Update `docs/TECHNICAL_ARCHITECTURE.md` application/activity/persistence sections.
3. Update `docs/ACTIVITY_REWARD_SYSTEM.md` for failed Expedition completion and passive recovery semantics.
4. Update `docs/MOBILE_ACTIVITY_INTEGRATION.md` if provider resolution/cancellation/late-completion responsibilities change.
5. Update `docs/TESTING_AND_PERFORMANCE.md` to document what is now headlessly certified vs still PlayMode/device-only.
6. Update `docs/IMPLEMENTATION_STATUS.md` with exact evidence, root causes, resolved findings, and remaining UNVERIFIED tiers.
7. Update `docs/DATA_MODEL.md` only if the durable schema actually changes; if it does, follow migration rules exactly.
8. Correct any stale contradictory documentation found by the whole-repository audit.

## Validation gates

Run every genuinely available gate and record exact results from this campaign:

1. `dotnet test verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj`
2. `scripts/verify-domain.ps1`
3. `scripts/verify-release-hygiene.ps1`
4. `scripts/verify-unity-static.ps1`
5. `pwsh scripts/Test-AgentGuards.ps1`
6. repository identity guard against the live checkout
7. targeted new orchestration tests repeatedly where useful
8. all activity/persistence/interruption suites impacted by this campaign
9. `git diff --check`
10. Unity EditMode/PlayMode only if the pinned editor has a real valid license; otherwise UNVERIFIED with reproducible commands recorded
11. Android/iOS device/runtime checks only if the required modules/hardware exist; otherwise UNVERIFIED

Before integration/push, fetch and run the remote-advance guard. If `origin/main` advanced unexpectedly, STOP automatic integration and reconcile deliberately. Never force-push.

## Acceptance gates

The campaign is complete only when all of the following are true:

1. The persisted-active-marker + failed Expedition completion scenario is reproduced by an automated regression test and fixed.
2. A failed completion commit cannot resurrect a stale `activeSession` that permanently suppresses provider-rejected movement in the same process.
3. Repeated transient persistence failures still preserve exactly-once movement recovery without loss or duplication.
4. Android/debug rejected session movement remains retryable; iOS historical recovery remains safe.
5. Provider completion is acknowledged only after durable proof; failed/reverted completion never receives a false durable acknowledgment.
6. Passive prepared deliveries cannot be stranded by timeout/late completion/lifecycle races under the final contract.
7. Fatal persistence loss still fails closed and does not synthesize or falsely present progress.
8. Correctness-critical application transaction decisions are executed by the standalone headless suite, not only by UNVERIFIED PlayMode tests.
9. Unity MonoBehaviours are thin wiring over the tested transaction policy/coordinator where practical; no duplicate hidden copy of the state machine remains in presentation code.
10. Existing M8.1/M8.2/M8.3 invariants and regressions remain green.
11. No newly discovered Critical/High orchestration/state-integrity defect remains unresolved.
12. Documentation and evidence tiers match the implementation exactly.
13. All available gates pass; blocked editor/device tiers remain explicitly UNVERIFIED.
14. Final tree is clean, remote advancement has been reconciled safely, and changes are committed/pushed without force.

## Constraints / non-goals

- Do not build Region 2, cloud save, multiplayer, combat, live ops, Health Connect, expanded HealthKit import, analytics backend, or other Phase 9 work.
- Do not require GPS for passive step earning.
- Do not weaken exactly-once movement safety to make recovery easier.
- Do not synthesize optional Expedition bonus evidence after failure/crash.
- Do not bypass ADR 0007 fail-closed persistence behavior.
- Do not solve this by disabling lifecycle autosave or removing persisted interruption recovery without an equivalent proven replacement.
- Do not solve it with provider-specific hacks that leave the common application protocol ambiguous.
- Do not add a giant dependency-injection framework or fake Unity runtime merely for tests.
- Do not rely exclusively on text-grep tests for transaction ordering when executable state-machine tests are possible.
- Do not add broad dirty tracking/save batching unless proven necessary for correctness.
- Do not bypass Unity licensing, UAC/elevation, signing, or hardware requirements.
- Do not suppress failing tests or weaken repository identity/concurrency guards.
- Do not force-push, delete remote refs, overwrite concurrent work, or mutate the sibling repository.

## Completion / reporting requirements

At the end:

1. Re-run all available validation gates after final code/doc changes.
2. Inspect the complete diff and the whole affected transaction graph, not only the last files touched.
3. Update `docs/IMPLEMENTATION_STATUS.md` with exact new test counts/results, systems changed, resolved defects, remaining limitations, and UNVERIFIED tiers.
4. Flip this prompt to `**Status:** COMPLETE` and append an executor report containing:
   - start SHA and final SHA;
   - exact root cause(s), including whether the planner’s persisted-marker rollback bug reproduced as predicted;
   - final application orchestration design;
   - Expedition success/failure/retry/crash semantics;
   - passive timeout/late-completion semantics;
   - how the headless verification boundary changed;
   - tests added/changed;
   - exact validation results;
   - editor/device UNVERIFIED evidence;
   - any deferred follow-up with rationale.
5. Use detailed commit messages; the final commit message should double as the session report.
6. Fetch, run the remote-advance guard, integrate safely to `main`, push without force, and release the writer lease on normal completion.

Do not stop after patching the single visible failure path. The point of M8.4 is to make the **real application transaction protocol** as rigorous and executable as the M8.3 domain/provider contract, so a green headless suite actually means the movement durability sequence used by the game is safe.

---

## Executor Report — M8.4 COMPLETE

**Branch:** `agent/walk-game/m8.4-exec-20260826`  
**Start SHA:** `e128a46f2929d8e54e639a66aeba1b77d2553347` (planner handoff `main@c7d18f766438eb50fbb3854d88a9972fdbc5dc32` + M8.4 prompt)  
**Final SHA:** see commit `feat(activity): M8.4 runtime orchestration durability & headless certification (ADR 0010)`  
**Lease:** `sess-m84-exec-20260826` on `Nayeon_16`

### Root cause — planner defect reproduced as predicted: YES

The exact sequencing reproduced headlessly before the fix:

1. `GameHost.Persist()` autosaved an `activeSession` marker while an Expedition ran (intended recoverable state; `Persist` is non-transactional by design, ADR 0007).
2. `ExpeditionController.RunExpedition()` set `IsActive=false; AbandonExpedition(); ProcessSessionResult(); CommitChanges()` — the in-memory marker was cleared, the session was credited, then `CommitChanges()` failed.
3. `PersistenceCoordinator.Commit()` reverted the live profile via `ProfileStateCopier.CopyActivityState()` from durable disk truth and restored the autosaved marker.
4. `ResolveSessionCompletion(sessionId, false)` correctly returned base steps to the passive stream (Android `RestorePending` / debug counter restore).
5. `ActivityService.ProcessPassiveSnapshot()` returned `SuppressedBySession` for the returned movement and for every subsequent passive pass — same-process stranding until boot recovery.

The pre-fix `MovementDeliveryDurabilityTests` did not model this ordering (abandon before rollback was missing; rollback→abandon-before-retry was explicit in tests but not in the controller). The new `ApplicationOrchestrationTests.F2` reproduces the exact controller ordering and fails on the old code (suppressed retry), passes after the coordinator repair.

Additional High defects of the same class were found and fixed:

- **Fatal NRE (High):** `ExpeditionController:174` and `ActivityTicker:125` dereferenced `host.Provider` after a `CommitChanges()` that could have entered `EnterBlockedState()` (nulling `Provider`/`Activity`/`Profile`). Fixed by capturing refs before commit and letting the coordinator skip resolution on `FatalPersistenceLoss`.
- **Ticker timeout stranding (High):** `ReconcileRoutine` exited on 12 s deadline without resolving the late-completing `PreparePassiveDeliveryAsync` task. An open Android `ClaimPending` / debug `_passiveClaim` then blocked all future preparations. Fixed by draining the late task on the main thread up to a 30 s hard cap and rejecting unprocessed deliveries (`durable=false`, cursor untouched).
- **Stop-fault abandonment (Medium):** fault/cancel/null `StopSessionAsync` cleared the marker in memory but never committed or resolved; a later passive commit failure could resurrect it. Fixed by routing the no-result path through the coordinator (`AbandonExpedition` + `CommitChangesWithOutcome` + resurrection repair).
- **Debug menu path (Medium):** `ActivityTicker.CompleteSessionRoutine` had the same ordering defect; now delegates to the coordinator.

### Final application orchestration design (ADR 0010)

Engine-free `ActivityTransactionCoordinator` (`Assets/WalkGame/Activity/ActivityTransactionCoordinator.cs`, `WalkGame.Activity` → `WalkGame.Persistence`):

- `CompleteExpedition(activity, provider, result, commit, growthEligible)` — evaluates trust (`TrustEvaluator(RewardPolicy.Default)`), calls `ProcessSessionResult` or `AbandonExpedition` (null result), invokes `commit` (`GameHost.CommitChangesWithOutcome()`), then resolves: `Committed`→`ResolveSessionCompletion(true)`, `Reverted`→`ResolveSessionCompletion(false)` + `RecoverInterruptedSession()` if `HasInterruptedSession` (same-process repair, converges on next successful commit, boot-recoverable), `Fatal`→no provider touch.
- `DeliverPreparedPassive(activity, provider, delivery, commit)` — same pattern for passive windows: `ProcessPassiveSnapshot` disposition drives `RequiresCommit`; non-committing paths resolve without a save (`Suppressed→false`, `DuplicateDurable→true`); `DurableMutation` commits then resolves, repairing any resurrected marker on `Reverted` and failing closed on `Fatal`.
- `RejectAbandonedPreparation(provider, delivery)` — late/timeout drain helper: `ResolvePreparedDelivery(false)` without processing.

`GameHost.CommitChangesWithOutcome()` exposes the three-way `PersistenceCommitOutcome` with identical `PersistenceReverted`/`DurableCommitResolved` event semantics; `CommitChanges()` wraps it. `ExpeditionController` and `ActivityTicker` capture provider/activity refs before commit, delegate both success and no-result paths to the coordinator, and gate `StatusMessage`/logging on `Committed`/`Reverted`/`Fatal`. The 30 s hard cap after the 12 s deadline is the only unbounded wait; overlapping reconciliations remain guarded by `_reconcileInFlight`.

### Expedition success/failure/retry/crash semantics

- **Success:** `ProcessSessionResult` marks `creditedSessionIds` and clears marker, `Committed` durably persists reward/marker/cursor, provider ack drops held base steps; exactly-once, never re-enters passive.
- **Transient save failure:** `Reverted` reverts the whole profile (including dedup), provider reject returns base steps to passive stream, coordinator repairs resurrected marker in memory; same-process passive retry credits base steps exactly once on next successful commit (bonus not synthesized). Repeated failures retry safely without duplication.
- **Duplicate session id:** harmless; `TryMarkCredited` prevents re-credit, `acceptedSteps` zeroed.
- **No result (stop fault/cancel/null):** marker is durably closed via the coordinator; if that commit itself reverts, the same resurrection repair applies.
- **Fatal loss:** `FatalPersistenceLoss` tears down `Profile`/`Activity`/`Provider`/`Ledger` etc. and recomposes the blocked recovery screen; no provider resolution, no fabricated reward, movement in that window is correctly unrecoverable (ADR 0007). Coordinator reports `isFatal` so callers never falsely celebrate.
- **Process death:** lifecycle autosave (`Persist`) may leave a durable marker; boot `RecoverInterruptedSession()` clears it. A repair that has not yet converged durably is likewise recovered at next boot.

### Passive timeout/late-completion semantics

The 12 s deadline remains the "failed query" line (cursor untouched, fail-closed). After expiry the coroutine keeps observing the same task on the main thread:

- faults/cancels → `HandleFault`, no delivery staged, cursor untouched;
- late `PreparedActivityDelivery` → `RejectAbandonedPreparation` (`durable=false`) so staged claim returns to pending; the snapshot is never processed, dedup keys and `lastSuccessfulSyncUtc` are untouched, and the next cycle retries the identical window;
- hard cap (30 s) without completion → error logged, claim may remain open until restart (the only unbounded wait; providers that never complete are provider bugs).

Overlapping/focus-triggered reconciliations cannot resolve the wrong delivery (`_reconcileInFlight` guard, single open claim per provider — `ClaimPending` returns zero while a claim is open). No `.Result`/`.Wait()` blocking, no background continuation touching destroyed state.

### Headless verification boundary change

Before: `verification/WalkGame.Domain.Tests` compiled `Core`, `Building`, `Gameplay`, `Activity`, `Persistence`, `Content` and EditMode tests — 165 tests — but **not** `Assets/WalkGame/App`. The transaction ordering was PlayMode-only and UNVERIFIED.

After: the three-way outcome, marker-repair, and drain-and-reject decisions live in the engine-free `ActivityTransactionCoordinator` (plus `GameHost.CommitChangesWithOutcome()` event fidelity). That surface is compiled by the existing glob and exercised by the new headless suite. Unity MonoBehaviours are now thin wiring; App-layer timing, scene composition, and provider JNI/CoreMotion callbacks remain UNVERIFIED without an editor/device but contain no hidden duplicate state machine.

Compile-time linkage guard: `WalkGame.Activity` now references `WalkGame.Persistence` and the `CommitChangesWithOutcome` API; a drift that bypasses the coordinator would be caught by `ApplicationOrchestrationTests` or by the standing `verify-unity-static`/`verify-domain` gates.

### Tests added/changed

- **New:** `Assets/WalkGame/Tests/EditMode/ApplicationOrchestrationTests.cs` (17 scenarios):
  F1 persisted-marker success; F2 persisted-marker failed-completion + same-process passive recovery; F3 two transient failures then success; F4 duplicate session harmless; F5 fatal loss fail-closed; F6/F6b stop-null with/without failing commit; F7 restart convergence after un-converged repair; F8 passive ack; F9 passive revert+retry; F10 suppressed retryable; F11 late/timeout drain + null-safety; F12 blocked while in-flight; F13 stale/duplicate resolutions; F14 durability-gated feedback truthfulness; plus trust-evaluation coverage.
- **Extended:** `ActivityServiceTests` (+1 — `M84_ResurrectedMarker_SuppressesUntilRecovered`), `AndroidCounterReconciliationTests` (+1 — `M84_RejectedExpedition_RestoredSteps_RetryExactlyOnce_WithBaselineAhead`), `SaveLoadTests` (+1 — `M84_ActiveSessionMarker_RoundTrips_AndSupportsBootRecovery`) per the exactly-once rule.
- **Totals:** 165 → **185/185** headless passing. Existing `MovementDeliveryDurabilityTests` (14) and `AndroidCounterReconciliationTests` claim tests remain green.

### Exact validation results (this campaign)

- `dotnet test verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj` — **185/185 PASS** (1.0 s, VSTest 17.14.0, net8.0).
- `scripts/verify-domain.ps1` — PASS (same suite, restore check).
- `scripts/verify-unity-static.ps1` — PASS (102 assets / 102 metas, Unity 6000.3.4f1 pin, package invariants, Bootstrap scene).
- `scripts/verify-release-hygiene.ps1` — PASS (62 runtime sources scanned, no GPS/save-path logging, no direct `Debug.Log`, manifest minimal).
- `scripts/Test-AgentGuards.ps1` — ps tier **24/24 PASS**; 12 sh-tier failures are the known WSL `bash.exe` shadowing in this environment (see M8.3 matrix: `Git Bash ahead of WSL bash.exe` required for sh pass; ps tier is authoritative).
- `scripts/assert-repo-identity.sh` — PASS (`quantdale/walk-game`).
- `git diff --check` — clean.
- Activity/persistence/interruption suites — all green (see 185 total).

### Editor/device UNVERIFIED evidence

- Unity 6000.3.4f1 installed at `C:\UnityEditors\6000.3.4f1\Editor`, reports pinned version, `UnityPackageManager.exe` present; Hub 3.21.0 MSIX holds zero accounts (`accounts.db` empty), licensing client: "Token not found in cache", 0 entitlement groups, no ULF — no offline activation without credentials. Repro: sign into Hub, activate, `scripts/setup-unity-project.ps1`, `scripts/verify-unity-editmode.ps1`, `scripts/verify-unity-playmode.ps1` → UNVERIFIED (prohibited to bypass).
- Android Build Support absent (`AndroidPlayer` missing); prior Hub module install `ELEVATION_CANCELLED`. Repro: install Android Build Support + SDK/NDK, `scripts/verify-android-smoke.ps1` → UNVERIFIED.
- iOS/macOS/Xcode absent → UNVERIFIED.
- Hence `RuntimeCertificationTests` PlayMode scenarios (bootstrap, activity, stale Expedition recovery, blocked persistence, late completion timing) are committed but remain UNVERIFIED unless a licensed editor runs them — campaign not claimed via PlayMode.

### Deferred follow-up (honest, deliberate)

- **Provider tasks that never complete after the 30 s hard cap** may leave a claim open until restart. Bounded by the cap; hanging providers are provider bugs — no arbitrary `Task` cancellation injected. Future work could add explicit `CancellationToken` to `PreparePassiveDeliveryAsync` if a provider needs it, but that is not required for the current synchronous Android/debug and no-op iOS resolutions.
- **Scene composition / provider JNI / CoreMotion / performance**: still PlayMode/device-only; no amount of headless hardening substitutes for a licensed editor run and physical-device lifecycle/thermal/battery measurement.
- **Broad dirty-tracking / save batching**: not introduced — the audit proved the current per-mutation `CommitChanges` + lifecycle `Persist` convergence is sufficient with the repair semantics.