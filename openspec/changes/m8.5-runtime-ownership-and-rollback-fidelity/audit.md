# M8.5 Audit — Runtime Ownership & Rollback Fidelity

**Status:** ACTIVE planning evidence  
**Repository:** `quantdale/walk-game`  
**Planned-From:** `main@616924fcbe61bc50a1c7f064b0fe6fe00fb185ba`  
**Base tree:** `57fcc79b598b69ed2c5bba86ff21536e40ed1e65`  
**Audit date:** 2026-08-26  
**Campaign class:** non-hardware hardening / provider lifecycle / transaction convergence / persistence fidelity

## 1. Executive finding

M8.4 materially improved the activity transaction protocol and raised the standalone gate to 185/185, but the repository still has a connected family of High-severity lifecycle and truthfulness gaps around **who owns an asynchronous provider operation, who releases a native provider, and whether rollback really reconstructs exact durable truth**.

The next highest-value unblocked campaign is therefore **M8.5 Runtime Ownership & Rollback Fidelity** rather than Region 2, art breadth, HealthKit/Health Connect, GPS, or speculative optimization.

This is not a generic cleanup campaign. The audit found concrete reachable defects after M8.4:

1. Android prepared-delivery resolution does not bind a resolution to the currently open delivery identity.
2. Passive preparation can outlive the ticker's hard timeout with no owner left to resolve a claim.
3. Provider/native resources have no explicit lifetime contract when `GameHost` blocks, rebuilds, starts over, or is destroyed.
4. `ExpeditionController` start/poll/stop tasks are unbounded and can outlive the controller/runtime.
5. A successful provider start followed by a domain `BeginExpedition` rejection can leave the provider session running.
6. The debug vehicle-session path bypasses the M8.4 `ActivityTransactionCoordinator` and can reintroduce the rollback-resurrected-session-marker defect.
7. Failed Expedition persistence can still leave positive `+steps -> +Vitality` reward copy visible even though the reward was rolled back.
8. Motion-permission UI subscribes with an anonymous handler that is never detached; its native task has no cancellation ownership.
9. `ProfileStateCopier` removes stale regions but not stale building/producer keys inside surviving regions, so failed commits do not always restore exact disk truth.
10. `CreditedActivityKeys.Rebuild()` does not canonicalize duplicate serialized entries before capacity eviction; a duplicate can cause the membership index to forget a still-present credited key.

These defects all violate existing repository promises rather than introducing new product scope.

## 2. Audit method and coverage

The planner enumerated the full recursive repository tree and reviewed the current `main` history, campaign handoffs, open PR/issue state, architecture docs, data model, activity/reward docs, mobile integration docs, privacy/safety docs, testing plan, package/project configuration, CI/static gates, application composition, native adapters, persistence pipeline, domain services, presentation controllers, and the activity/save/runtime test suites.

Logic-bearing surfaces were traced semantically. Unity `.meta` files and serialized project assets were treated structurally through the repository's asset/meta/static-hygiene contract rather than pretending metadata is executable logic. No implementation code was changed during this planner audit.

Representative audited surfaces include:

- `.agent/**`, `AGENTS.md`, `.github/workflows/**`, `scripts/**`.
- `Assets/WalkGame/Core/**`.
- `Assets/WalkGame/Activity/**`.
- `Assets/WalkGame/Persistence/**`.
- `Assets/WalkGame/Gameplay/**`.
- `Assets/WalkGame/Building/**`.
- `Assets/WalkGame/App/**`.
- `Assets/WalkGame/UI/**`.
- `Assets/WalkGame/World/**`.
- Android Kotlin/JNI bridge and Android manifest.
- iOS Objective-C/CoreMotion bridge.
- EditMode/headless tests and PlayMode runtime certification.
- `verification/WalkGame.Domain.Tests/**`.
- `docs/**` required by the repository planning order.

The audit also searched the activity transaction family end-to-end: `BeginExpedition`, `AbandonExpedition`, `RecoverInterruptedSession`, `ProcessSessionResult`, `PreparePassiveDeliveryAsync`, `ResolvePreparedDelivery`, `ResolveSessionCompletion`, `StartSessionAsync`, `PollSessionAsync`, `StopSessionAsync`, `CommitChanges`, `Persist`, lifecycle callbacks, persistence events, task observation, and timeout/fault branches.

## 3. Repository state reconciled

At planning time:

- `main` points to `616924fcbe61bc50a1c7f064b0fe6fe00fb185ba`.
- The latest implementation campaign is M8.4 / ADR 0010.
- `.agent/EXECUTION_PROMPT.md` is COMPLETE for M8.4, so there is no implementation campaign to resume.
- There are no open PRs and no open issues competing with the next campaign.
- Last executor evidence reports `dotnet test` at **185/185**, Unity static verification at **102 assets / 102 metas**, release hygiene over **62 runtime sources**, and agent guard success on the available PowerShell path.
- Those counts are historical evidence only. The M8.5 executor MUST rerun every locally available gate and report fresh results.
- Unity compile/EditMode/PlayMode, Android Build Support/real step sensor, iOS/Xcode, and physical device performance remain UNVERIFIED in the current documented environment.

## 4. Confirmed findings

### H1 — Android stale delivery resolution can resolve the wrong claim

`AndroidStepSensorProvider.ResolvePreparedDelivery` checks that a delivery exists and that the reconciler has an open claim, then acknowledges/restores the reconciler claim. It does not compare `delivery.deliveryId` against the identity of the currently open claim.

The interface contract says stale/repeated resolutions must be harmless. The debug provider enforces delivery identity, but the actual Android adapter does not. A late resolution for delivery A can therefore acknowledge or restore a newer claim B.

**Required disposition:** introduce claim identity into an engine-free/testable Android claim layer and prove stale, repeated, null, and unknown resolutions cannot mutate a newer claim.

### H2 — passive preparation can be orphaned after the hard drain ceiling

`ActivityTicker` gives `PreparePassiveDeliveryAsync` 12 seconds, then continues observing for 30 more seconds. If the task still does not complete it logs that a provider claim may remain open until process restart, exits, and releases `_reconcileInFlight`.

That behavior contradicts the stronger documentation wording that late claims cannot be stranded. With no cancellation/abort/operation-ownership contract, a task can complete after the coroutine has abandoned it and no runtime owner remains to reject its prepared delivery.

**Required disposition:** make provider operations cancelable/owned or provide an equivalent deterministic abandon protocol. Race-at-timeout semantics must yield exactly one owner/resolution.

### H3 — native provider lifetime is implicit and leaks across runtime replacement

`GameHost` can enter blocked persistence, retry loading, start over with a fresh profile, or be destroyed. Those paths drop/rebuild `Provider` references, but `IActivityProvider` has no shutdown/disposal contract and `GameHost` does not release the old provider first.

The Android native bridge exposes `stopMonitoring`, while the iOS bridge exposes `WG_StopPedometerUpdates`; neither is part of provider disposal from the composition root. A same-process replacement can therefore leave an old listener/session alive. On iOS a new provider can observe the global native live session as already running.

**Required disposition:** explicit idempotent provider teardown, invoked before provider/profile graph replacement and on host destruction. Teardown must stop transient native work without fabricating a durable acknowledgment.

### H4 — Expedition asynchronous operations have no bounded owner lifetime

`ExpeditionController` observes provider start, poll, and stop tasks with coroutine loops that have no cancellation token, timeout, generation guard, or controller-destruction invalidation.

If the provider hangs or the runtime is recomposed while an operation is outstanding, the task can outlive the controller/runtime. The same class of ownership gap exists in motion-permission operations.

**Required disposition:** define operation ownership/cancellation semantics across active-session and permission paths. Destruction/recomposition must invalidate or cancel old work; late completions must not mutate the new runtime.

### H5 — provider start can succeed while domain session acquisition fails

After `StartSessionAsync` succeeds, `ExpeditionController` calls `Activity.BeginExpedition`. If the domain rejects acquisition, the coroutine exits without stopping/aborting the already-started provider session. The debug completion path has an equivalent shape.

**Required disposition:** provider start and domain session ownership must converge. A start that cannot be adopted by the domain is explicitly aborted/stopped before returning.

### H6 — debug vehicle session bypasses the M8.4 transaction coordinator

`UiComposer.VehicleSessionRoutine` starts/stops the debug provider, manually evaluates trust, calls `Activity.ProcessSessionResult`, commits, and resolves the provider itself. Its stop-fault/null branch calls only `AbandonExpedition` without a durable close.

That is a second application transaction path after M8.4 centralized normal Expedition and ticker debug completion. A failed commit can restore a previously durable `activeSession` marker and the vehicle path does not perform the coordinator's post-rollback repair.

**Required disposition:** all active-session completion/no-result/failure flows delegate to one coordinator protocol. Grep the entire repo and remove equivalent direct completion sequences.

### H7 — failed Expedition can display a rolled-back reward as if earned

After coordinator completion, `ExpeditionController` always assigns `LastResult` and builds `LastRewardMessage` from the processed result, regardless of commit outcome. `UiComposer.GetExpeditionProgress` displays that positive reward string when the Expedition is inactive.

If persistence reverted, canonical Vitality is rolled back but the HUD can still show `+steps -> +Vitality` alongside the save-failure status.

**Required disposition:** success-only reward presentation must be gated by proven durability. Reverted/fatal outcomes clear success reward copy and success-only feedback.

### M1 — motion permission callback can outlive the UI that owns it

`UiComposer` subscribes to `MotionPermissionCoordinator.StateChanged` through an anonymous lambda and does not detach it in `OnDestroy`. Permission refresh/request tasks are also uncancelled; the iOS implementation can run for an extended native authorization round.

**Required disposition:** named detachable handler plus owner cancellation/invalidation. Late permission completion after UI teardown is harmless.

### M2 — audio settings can diverge from reverted canonical state

Audio values are applied to `FeedbackController` before `CommitChanges`. If the commit fails and `ProfileStateCopier` restores older settings, the live ambience source is not explicitly reapplied from the reverted profile.

**Required disposition:** on persistence rollback, presentation-side audio state is refreshed from canonical settings. Do not redesign audio.

### H8 — rollback copier does not remove stale nested dictionary keys

`ProfileStateCopier.CopyWorldState` removes stale `regionStates` keys after copying survivors. Inside a surviving region it adds/updates source `buildingStates` and `producerStates` entries but does not remove target entries absent from the durable source.

A failed commit can therefore leave extra nested state in memory after a rollback, violating ADR 0007's exact-disk-truth contract.

**Required disposition:** prune stale nested keys after preserving/reusing surviving value instances. Extend the graph-fidelity test with deliberately dirty target-only keys.

### H9 — duplicate serialized dedup entries can reopen a credited key during compaction

`CreditedActivityKeys.Rebuild` rebuilds `_set` from `entries` but retains duplicate list entries. Capacity trimming removes the oldest list item and also removes that key from `_set`. If the same key remains later in `entries`, membership can become false even though the durable list still contains it, allowing a future `TryMarkCredited` to accept the key again.

`SaveValidator` calls `Rebuild` on load, so malformed/hand-edited/corrupt duplicate input can reach this behavior.

**Required disposition:** canonicalize to unique entries with deterministic most-recent ordering before enforcing capacity; add corruption/over-capacity tests proving no credited key is reopened.

## 5. Areas reviewed without a campaign-level defect

The audit did not find a Critical/High reason to expand M8.5 into the following systems:

- restoration prerequisite/economy transaction rules;
- placement footprint/rotation validation;
- canonical Builder/Explore transform projection;
- offline production arithmetic/caps;
- world/environment presentation;
- save file quarantine/forward-schema refusal algorithm;
- reward cap/trust formulas;
- privacy policy or permission-scope expansion;
- content breadth/Region 2.

Any defect discovered by the executor while implementing M8.5 should still be fixed if it is Critical/High correctness or a Medium integrity issue necessary to preserve the M8.5 invariants. Do not use that rule to turn the campaign into unrelated feature work.

## 6. Documentation mismatches to repair

Current architecture/mobile docs describe the 30-second late-drain behavior more strongly than the code supports. M8.5 must make implementation and documentation converge: either the new ownership contract truly guarantees no orphaned provider claim, or the docs must state the real bounded guarantee. The target is the stronger guarantee.

The `ExpeditionController` documentation also describes itself as the only runtime completion path while `UiComposer.VehicleSessionRoutine` is an independent path. M8.5 must make that statement true at the transaction-protocol level.

## 7. Why this campaign comes before device readiness

The formal roadmap moves toward M8 Device Ready / M9 Playtest Validated, but the documented environment cannot currently prove Unity editor, Android sensor, iOS/Xcode, or physical performance gates. More importantly, device testing is lower-value while provider lifetime and rollback invariants are still leaky.

Repository priority explicitly places state integrity and movement reward correctness ahead of performance and content breadth. M8.5 is the last high-leverage, headlessly testable correctness tranche exposed by this audit. After it, the planner should prefer real editor/device certification when the environment allows it rather than inventing more speculative hardening.

## 8. Planning boundary

This audit is evidence for implementation; it is not implementation itself.

The executor MUST:

- reacquire current repository truth after pull/fetch;
- preserve any equivalent fix that landed after the Planned-From SHA;
- execute the normative spec and tasks in this change package;
- author ADR 0011 for provider lifetime/operation ownership if no newer ADR already supersedes it;
- produce fresh test/static evidence;
- leave unavailable hardware/editor tiers explicitly UNVERIFIED;
- update implementation status and close this OpenSpec change only when all locally executable acceptance criteria pass.
