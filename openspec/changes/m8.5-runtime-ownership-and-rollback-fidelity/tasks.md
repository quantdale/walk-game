# M8.5 Tasks — Runtime Ownership & Rollback Fidelity

**Status:** ACTIVE  
**Executor rule:** implement the entire change in one coherent campaign. Fix every discovered Critical/High correctness defect and any Medium integrity defect required to satisfy these invariants. Do not expand into unrelated features.

## 0. Reconcile repository truth before editing

- [ ] Run `sh scripts/assert-repo-identity.sh` or `./scripts/Assert-RepoIdentity.ps1`. STOP on mismatch.
- [ ] Read `AGENTS.md`, `.agent/PLANNER_HANDOFF.md`, this OpenSpec change, current `docs/IMPLEMENTATION_STATUS.md`, roadmap, architecture, data model, activity/mobile/privacy/testing docs, ADR 0007/0009/0010, and any newer ADR.
- [ ] Fetch remote state and record current branch, HEAD, upstream, worktree status, `origin/main`, recent commits, and open PRs/issues.
- [ ] Confirm the repository is `quantdale/walk-game`, never `quantdale/simple-walk-game`.
- [ ] If `origin/main` advanced beyond `616924fcbe61bc50a1c7f064b0fe6fe00fb185ba`, inspect every intervening commit/diff and reconcile this plan; preserve equivalent landed fixes.
- [ ] Acquire the repository writer lease before mutation. Use one writer, one worktree, one campaign branch such as `agent/walk-game/m8.5-<session-id>`.
- [ ] Run every locally available baseline gate and record fresh evidence. The prior 185/185 count is historical, not this campaign's baseline.

## 1. Write failing regressions for the planner findings first

Before production fixes, add the lowest-layer deterministic tests that reproduce the actual invariants.

- [ ] Android claim A stale resolution cannot acknowledge claim B.
- [ ] Android claim A stale rejection cannot restore/alter claim B.
- [ ] null/unknown/repeated delivery identities are no-ops.
- [ ] timeout/cancel versus completion race has exactly one terminal owner.
- [ ] a provider operation abandoned by its Unity owner cannot strand provider-private movement.
- [ ] provider start success + domain `BeginExpedition` rejection leaves provider idle.
- [ ] active-session completion through the vehicle/debug fixture on failed persistence performs the same rollback-marker repair as normal Expedition completion.
- [ ] stop fault/cancel/null result closes/repairs the canonical session through the shared transaction path.
- [ ] failed Expedition persistence produces no positive durable reward presentation state.
- [ ] rollback into a target containing extra building/producer keys removes those target-only keys while preserving surviving object identity.
- [ ] duplicate/over-capacity `CreditedActivityKeys.entries` rebuild into unique bounded recent entries without reopening credited membership.

Where a behavior currently exists only in Unity App code, extract the smallest engine-free protocol needed for a real headless regression instead of writing a fake test that cannot fail on the production ordering.

## 2. Introduce the provider lifetime and operation-ownership contract

- [ ] Design the smallest explicit provider shutdown/disposal contract satisfying `design.md` I1–I4.
- [ ] Add cancellation/operation ownership to async provider operations, preferably via `CancellationToken` where compatible, or an equivalent deterministic lease/generation primitive.
- [ ] Ensure shutdown is idempotent and prevents new native work.
- [ ] Ensure shutdown/cancel is **not** treated as durable acknowledgment of movement.
- [ ] Add deterministic tests for shutdown repetition, operation completion after owner invalidation, and completion/cancel races.
- [ ] Update all `IActivityProvider` implementations together: Debug, Unavailable, Android, iOS.
- [ ] Do not add a broad async framework or external dependency for this.

Likely files:

- `Assets/WalkGame/Activity/IActivityProvider.cs`
- `Assets/WalkGame/Activity/DebugActivityProvider.cs`
- `Assets/WalkGame/Activity/UnavailableActivityProvider.cs`
- new engine-free operation/lifetime helper only if required
- Android/iOS provider adapters

## 3. Fix Android prepared-delivery identity

- [ ] Refactor the engine-free Android reconciler claim model so the open claim has identity, not just an amount/boolean.
- [ ] Bind `PreparedActivityDelivery.deliveryId` to that claim identity.
- [ ] `ResolvePreparedDelivery` must resolve only the named current claim.
- [ ] Repeated/stale/unknown/null resolution must be harmless.
- [ ] Preserve exact retry behavior after failed commit.
- [ ] Preserve reboot reset, anomaly rebaseline, persisted cursor, and process-restart reconstruction semantics.
- [ ] Extend `AndroidCounterReconciliationTests` and movement durability tests with actual claim identity transitions.

Do not settle for a provider-only string comparison if the underlying engine-free claim can still be mutated without identity.

## 4. Make passive reconcile ownership bounded without orphaning late claims

- [ ] Replace the current “12 s soft timeout + 30 s hard abandon” ownership hole.
- [ ] Preserve a reasonable scheduling timeout but ensure a late provider completion always has a deterministic terminal cleanup owner.
- [ ] On timeout, reward/cursor state remains unchanged unless a completed delivery is actually adopted and committed.
- [ ] Pending movement remains retryable.
- [ ] Next reconcile is not permanently blocked by an old claim.
- [ ] A completion arriving at the same time as cancellation is processed/rejected exactly once.
- [ ] Old runtime/provider-generation completions cannot touch a newer provider claim.
- [ ] Keep `ActivityProcessed`/HUD refresh behavior sensible after timeout/fault/suppression.

Primary file: `Assets/WalkGame/App/ActivityTicker.cs`, with protocol logic extracted engine-free where needed.

## 5. Bound Expedition start/poll/stop ownership

- [ ] Add owner cancellation/invalidation to start, poll, and stop observations.
- [ ] On controller/runtime destruction, old tasks cannot mutate UI or canonical state.
- [ ] Add a deliberate policy for hung poll/stop operations; do not wait forever on the Unity main-loop coroutine.
- [ ] Provider start success is not final until the domain adopts the Expedition.
- [ ] If `Activity.BeginExpedition` rejects, stop/abort the provider session before returning.
- [ ] Preserve pause/focus behavior without manufacturing movement or rewarding stale samples.
- [ ] A late sample/result from an old provider generation is harmless.

Primary files:

- `Assets/WalkGame/App/ExpeditionController.cs`
- `Assets/WalkGame/App/TaskObservation.cs` or replacement helper if necessary
- provider implementations

## 6. Make GameHost own provider teardown

Audit every service-graph destruction/rebuild path and enforce teardown-before-drop.

- [ ] fatal persistence / blocked-state transition;
- [ ] retry-load success and runtime reconstruction;
- [ ] start-over flow;
- [ ] host destruction/application quit;
- [ ] any shared rebuild helper introduced during the campaign.

Provider-specific requirements:

- [ ] Android teardown invokes native step-monitor stop and cannot leave duplicate sensor listeners after same-process reconstruction.
- [ ] iOS teardown stops live pedometer updates and invalidates old callback ownership.
- [ ] same-process fresh provider no longer sees `AlreadyRunning` solely because the previous provider leaked its native live session.
- [ ] teardown failure is contained/logged and never invents durable reward state.

Primary file: `Assets/WalkGame/App/GameHost.cs` plus platform adapters/native bridge as required.

## 7. Eliminate secondary active-session transaction paths

Search the whole repository again for direct combinations of:

`ProcessSessionResult`, `AbandonExpedition`, `CommitChanges*`, `ResolveSessionCompletion`, `StopSessionAsync`, `BeginExpedition`.

- [ ] Route `UiComposer.VehicleSessionRoutine` through the same sanctioned active-session completion/no-result protocol as `ExpeditionController`.
- [ ] Keep debug vehicle suspicion/trust fixture behavior, but do not duplicate transaction sequencing.
- [ ] Verify `ActivityTicker.CompleteSessionRoutine` remains on the shared coordinator path and adopts the new start-abort/lifetime rules.
- [ ] Remove or refactor any other direct completion sequence found by the executor.
- [ ] Update misleading comments/docs that claim one path while another exists.

The end state should make `ActivityTransactionCoordinator` (or one explicitly documented successor) the only authority for process -> commit -> provider resolve -> post-rollback repair.

## 8. Fix durability-gated player presentation

### Expedition result

- [ ] On committed completion, show the positive reward summary.
- [ ] On reverted completion, clear positive `+steps -> +Vitality` copy and show only truthful unsaved/retryable movement copy.
- [ ] On fatal completion, show recovery copy only.
- [ ] Ensure success-only Expedition finish feedback/haptic/audio cannot fire after a reverted/fatal save.
- [ ] Move the Expedition start cue so it represents actual successful provider + domain session adoption, not merely a button tap.

### Permission lifetime

- [ ] Replace anonymous `MotionPermissionCoordinator.StateChanged` subscription with a named detachable handler.
- [ ] Cancel/invalidate refresh/request observations during UI/runtime teardown.
- [ ] Late OS/native permission completion cannot refresh destroyed UI or a new coordinator generation.
- [ ] Denied/unavailable remains normal and non-blocking.

### Audio settings rollback

- [ ] If an audio-setting commit reverts, reapply actual profile values to runtime audio sources.
- [ ] Add focused coverage at the lowest viable layer and PlayMode source where needed.

Primary files:

- `ExpeditionController.cs`
- `UiComposer.cs`
- `FeedbackController.cs`
- `MotionPermissionCoordinator.cs`
- HUD/UI tests as appropriate

## 9. Repair persistence rollback graph fidelity

- [ ] In `ProfileStateCopier`, preserve identity of surviving regions/buildings/producers while copying durable values.
- [ ] Remove target-only building keys in surviving regions.
- [ ] Remove target-only producer keys in surviving regions.
- [ ] Audit other serialized nested maps for the same stale-key pattern; fix equivalent omissions required for exact graph equality.
- [ ] Extend `SaveIntegrityApplicationTests.CopyInto_MatchesSerializedGraph_Exactly` or a companion test so the **target starts dirty** with extra nested keys and still serializes exactly like source after copy.
- [ ] Keep the existing object-identity assertions green.

Do not replace the whole profile root to make the test pass.

## 10. Canonicalize dedup rebuild safely

- [ ] Update `CreditedActivityKeys.Rebuild()` to remove null/empty values.
- [ ] Collapse duplicates preserving most-recent occurrence order.
- [ ] Apply capacity to the unique sequence, oldest first.
- [ ] Rebuild membership exactly from the final entries.
- [ ] Add tests for duplicates below capacity, duplicates across the eviction boundary, all-duplicate input, null/empty corruption, and over-capacity unique data.
- [ ] Prove `TryMarkCredited` rejects every surviving credited key after rebuild/save/load.
- [ ] Run existing exact-once activity/save/restart tests to ensure no regression.

No schema bump is expected for this repair unless the persisted shape changes.

## 11. Add ADR 0011 and reconcile documentation

- [ ] Add `docs/adr/0011-...md` or the next available ADR number if main advanced.
- [ ] Explain provider lifetime, operation ownership/cancellation, cancellation-vs-acknowledgment, generation invalidation, timeout race semantics, and start adoption.
- [ ] Update `docs/TECHNICAL_ARCHITECTURE.md`.
- [ ] Update `docs/MOBILE_ACTIVITY_INTEGRATION.md`.
- [ ] Update `docs/ACTIVITY_REWARD_SYSTEM.md` where exact-once/lifecycle wording changed.
- [ ] Update `docs/TESTING_AND_PERFORMANCE.md` with new regression coverage and honest verification status.
- [ ] Update `docs/DATA_MODEL.md` only if persisted semantics/repair policy need clarification.
- [ ] Update `docs/IMPLEMENTATION_STATUS.md` with an M8.5 matrix and exact fresh evidence.
- [ ] Remove the current documentation mismatch that implies the 30-second drain ceiling itself guarantees no stranded claim.

## 12. Mandatory validation matrix

Run every available gate after implementation and record exact command/result.

- [ ] `dotnet test verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj`
- [ ] `./scripts/verify-domain.ps1` or repository-supported shell equivalent
- [ ] `./scripts/verify-unity-static.ps1`
- [ ] `./scripts/verify-release-hygiene.ps1`
- [ ] `./scripts/Test-AgentGuards.ps1` and supported shell guard where environment permits
- [ ] repository identity guard
- [ ] `git diff --check`
- [ ] Unity compile/EditMode/PlayMode if a licensed editor is genuinely available
- [ ] Android build/device checks if Build Support + suitable device are genuinely available
- [ ] iOS build/device checks only with macOS/Xcode/signing environment

Do not label unavailable Unity/device tiers as PASS. Record `UNVERIFIED — <reason>`.

### Required functional acceptance scenarios

- [ ] F1 stale Android prepared delivery cannot mutate newer claim.
- [ ] F2 repeated/null/unknown Android resolution is no-op.
- [ ] F3 passive timeout/cancel leaves movement retryable and no open claim stranded.
- [ ] F4 cancellation/completion race has exactly one terminal owner.
- [ ] F5 fatal persistence transition shuts provider down before graph disposal.
- [ ] F6 retry/start-over in same process constructs a clean provider with no leaked native listener/session.
- [ ] F7 host destruction releases provider idempotently.
- [ ] F8 provider start success + domain begin rejection explicitly aborts provider session.
- [ ] F9 poll/stop/permission operations cannot retain dead UI/runtime ownership indefinitely.
- [ ] F10 vehicle/debug completion uses shared coordinator and repairs failed-commit marker resurrection.
- [ ] F11 stop fault/cancel/null result uses shared durable close/repair semantics.
- [ ] F12 reverted/fatal Expedition shows no positive reward success copy/cue.
- [ ] F13 committed Expedition still shows correct result and reward.
- [ ] F14 failed audio setting commit reapplies reverted canonical audio values.
- [ ] F15 dirty-target rollback copy serializes exactly like durable source and preserves surviving identities.
- [ ] F16 corrupted duplicate dedup input canonicalizes safely and cannot reopen credited membership.
- [ ] F17 existing reboot/process-death/exact-once activity scenarios remain green.
- [ ] F18 passive movement still requires no GPS and no new sensitive permission was introduced.

## 13. Completion / handoff

- [ ] Re-run repository-wide search for every activity-provider operation, resolve path, service rebuild, and direct completion sequence. Confirm no unsanctioned path remains.
- [ ] Reconcile all code comments/docs with actual implementation.
- [ ] Mark completed OpenSpec tasks only with evidence.
- [ ] Change this change's status to COMPLETE only when all locally executable requirements are satisfied.
- [ ] Replace `.agent/EXECUTION_PROMPT.md` status with COMPLETE and a detailed execution report: start SHA, branch, final SHA, defects fixed, design decisions, tests/gates, blocked external tiers, docs/ADR updates, and remaining real follow-up.
- [ ] Commit with a detailed session report and push the campaign branch per repository workflow.

If all M8.5 correctness work closes without a new High blocker, explicitly recommend **Unity/device certification** as the next campaign rather than inventing Region 2 or another speculative hardening tranche.
