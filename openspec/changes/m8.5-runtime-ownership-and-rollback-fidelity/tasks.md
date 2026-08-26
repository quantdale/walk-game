# M8.5 Tasks — Runtime Ownership & Rollback Fidelity

**Status:** COMPLETE
**Executor rule:** implement the entire change in one coherent campaign. Fix every discovered Critical/High correctness defect and any Medium integrity defect required to satisfy these invariants. Do not expand into unrelated features.

**Completion evidence (executor, 2026-08-26):** all tasks executed on branch `agent/walk-game/m8.5-exec-20260826` from base `fb619e9`. Headless suite **213/213 PASS** (fresh `dotnet test` + `verify-domain.ps1`); `verify-unity-static.ps1` 107/107; `verify-release-hygiene.ps1` 63 sources; agent guards 24/24 ps tier (sh tier blocked by known WSL bash.exe shadowing); repo identity guard exit 0 pre/post; `git diff --check` clean. F1–F18 functional scenarios covered by the new/extended suites listed below and in `docs/IMPLEMENTATION_STATUS.md` § M8.5. Unity EditMode/PlayMode and Android/iOS device tiers remain honestly **UNVERIFIED** (no licensed editor / Build Support / macOS).

## 0. Reconcile repository truth before editing

- [x] Run `sh scripts/assert-repo-identity.sh` or `./scripts/Assert-RepoIdentity.ps1`. STOP on mismatch.
- [x] Read `AGENTS.md`, `.agent/PLANNER_HANDOFF.md`, this OpenSpec change, current `docs/IMPLEMENTATION_STATUS.md`, roadmap, architecture, data model, activity/mobile/privacy/testing docs, ADR 0007/0009/0010, and any newer ADR.
- [x] Fetch remote state and record current branch, HEAD, upstream, worktree status, `origin/main`, recent commits, and open PRs/issues.
- [x] Confirm the repository is `quantdale/walk-game`, never `quantdale/simple-walk-game`.
- [x] If `origin/main` advanced beyond `616924fcbe61bc50a1c7f064b0fe6fe00fb185ba`, inspect every intervening commit/diff and reconcile this plan; preserve equivalent landed fixes.
- [x] Acquire the repository writer lease before mutation. Use one writer, one worktree, one campaign branch such as `agent/walk-game/m8.5-<session-id>`.
- [x] Run every locally available baseline gate and record fresh evidence. The prior 185/185 count is historical, not this campaign's baseline.

## 1. Write failing regressions for the planner findings first

Before production fixes, add the lowest-layer deterministic tests that reproduce the actual invariants.

- [x] Android claim A stale resolution cannot acknowledge claim B.
- [x] Android claim A stale rejection cannot restore/alter claim B.
- [x] null/unknown/repeated delivery identities are no-ops.
- [x] timeout/cancel versus completion race has exactly one terminal owner.
- [x] a provider operation abandoned by its Unity owner cannot strand provider-private movement.
- [x] provider start success + domain `BeginExpedition` rejection leaves provider idle.
- [x] active-session completion through the vehicle/debug fixture on failed persistence performs the same rollback-marker repair as normal Expedition completion.
- [x] stop fault/cancel/null result closes/repairs the canonical session through the shared transaction path.
- [x] failed Expedition persistence produces no positive durable reward presentation state.
- [x] rollback into a target containing extra building/producer keys removes those target-only keys while preserving surviving object identity.
- [x] duplicate/over-capacity `CreditedActivityKeys.entries` rebuild into unique bounded recent entries without reopening credited membership.

Where a behavior currently exists only in Unity App code, extract the smallest engine-free protocol needed for a real headless regression instead of writing a fake test that cannot fail on the production ordering.

## 2. Introduce the provider lifetime and operation-ownership contract

- [x] Design the smallest explicit provider shutdown/disposal contract satisfying `design.md` I1–I4.
- [x] Add cancellation/operation ownership to async provider operations, preferably via `CancellationToken` where compatible, or an equivalent deterministic lease/generation primitive.
- [x] Ensure shutdown is idempotent and prevents new native work.
- [x] Ensure shutdown/cancel is **not** treated as durable acknowledgment of movement.
- [x] Add deterministic tests for shutdown repetition, operation completion after owner invalidation, and completion/cancel races.
- [x] Update all `IActivityProvider` implementations together: Debug, Unavailable, Android, iOS.
- [x] Do not add a broad async framework or external dependency for this.

Likely files:

- `Assets/WalkGame/Activity/IActivityProvider.cs`
- `Assets/WalkGame/Activity/DebugActivityProvider.cs`
- `Assets/WalkGame/Activity/UnavailableActivityProvider.cs`
- new engine-free operation/lifetime helper only if required
- Android/iOS provider adapters

## 3. Fix Android prepared-delivery identity

- [x] Refactor the engine-free Android reconciler claim model so the open claim has identity, not just an amount/boolean.
- [x] Bind `PreparedActivityDelivery.deliveryId` to that claim identity.
- [x] `ResolvePreparedDelivery` must resolve only the named current claim.
- [x] Repeated/stale/unknown/null resolution must be harmless.
- [x] Preserve exact retry behavior after failed commit.
- [x] Preserve reboot reset, anomaly rebaseline, persisted cursor, and process-restart reconstruction semantics.
- [x] Extend `AndroidCounterReconciliationTests` and movement durability tests with actual claim identity transitions.

Do not settle for a provider-only string comparison if the underlying engine-free claim can still be mutated without identity.

## 4. Make passive reconcile ownership bounded without orphaning late claims

- [x] Replace the current “12 s soft timeout + 30 s hard abandon” ownership hole.
- [x] Preserve a reasonable scheduling timeout but ensure a late provider completion always has a deterministic terminal cleanup owner.
- [x] On timeout, reward/cursor state remains unchanged unless a completed delivery is actually adopted and committed.
- [x] Pending movement remains retryable.
- [x] Next reconcile is not permanently blocked by an old claim.
- [x] A completion arriving at the same time as cancellation is processed/rejected exactly once.
- [x] Old runtime/provider-generation completions cannot touch a newer provider claim.
- [x] Keep `ActivityProcessed`/HUD refresh behavior sensible after timeout/fault/suppression.

Primary file: `Assets/WalkGame/App/ActivityTicker.cs`, with protocol logic extracted engine-free where needed.

## 5. Bound Expedition start/poll/stop ownership

- [x] Add owner cancellation/invalidation to start, poll, and stop observations.
- [x] On controller/runtime destruction, old tasks cannot mutate UI or canonical state.
- [x] Add a deliberate policy for hung poll/stop operations; do not wait forever on the Unity main-loop coroutine.
- [x] Provider start success is not final until the domain adopts the Expedition.
- [x] If `Activity.BeginExpedition` rejects, stop/abort the provider session before returning.
- [x] Preserve pause/focus behavior without manufacturing movement or rewarding stale samples.
- [x] A late sample/result from an old provider generation is harmless.

Primary files:

- `Assets/WalkGame/App/ExpeditionController.cs`
- `Assets/WalkGame/App/TaskObservation.cs` or replacement helper if necessary
- provider implementations

## 6. Make GameHost own provider teardown

Audit every service-graph destruction/rebuild path and enforce teardown-before-drop.

- [x] fatal persistence / blocked-state transition;
- [x] retry-load success and runtime reconstruction;
- [x] start-over flow;
- [x] host destruction/application quit;
- [x] any shared rebuild helper introduced during the campaign.

Provider-specific requirements:

- [x] Android teardown invokes native step-monitor stop and cannot leave duplicate sensor listeners after same-process reconstruction.
- [x] iOS teardown stops live pedometer updates and invalidates old callback ownership.
- [x] same-process fresh provider no longer sees `AlreadyRunning` solely because the previous provider leaked its native live session.
- [x] teardown failure is contained/logged and never invents durable reward state.

Primary file: `Assets/WalkGame/App/GameHost.cs` plus platform adapters/native bridge as required.

## 7. Eliminate secondary active-session transaction paths

Search the whole repository again for direct combinations of:

`ProcessSessionResult`, `AbandonExpedition`, `CommitChanges*`, `ResolveSessionCompletion`, `StopSessionAsync`, `BeginExpedition`.

- [x] Route `UiComposer.VehicleSessionRoutine` through the same sanctioned active-session completion/no-result protocol as `ExpeditionController`.
- [x] Keep debug vehicle suspicion/trust fixture behavior, but do not duplicate transaction sequencing.
- [x] Verify `ActivityTicker.CompleteSessionRoutine` remains on the shared coordinator path and adopts the new start-abort/lifetime rules.
- [x] Remove or refactor any other direct completion sequence found by the executor.
- [x] Update misleading comments/docs that claim one path while another exists.

The end state should make `ActivityTransactionCoordinator` (or one explicitly documented successor) the only authority for process -> commit -> provider resolve -> post-rollback repair.

## 8. Fix durability-gated player presentation

### Expedition result

- [x] On committed completion, show the positive reward summary.
- [x] On reverted completion, clear positive `+steps -> +Vitality` copy and show only truthful unsaved/retryable movement copy.
- [x] On fatal completion, show recovery copy only.
- [x] Ensure success-only Expedition finish feedback/haptic/audio cannot fire after a reverted/fatal save.
- [x] Move the Expedition start cue so it represents actual successful provider + domain session adoption, not merely a button tap.

### Permission lifetime

- [x] Replace anonymous `MotionPermissionCoordinator.StateChanged` subscription with a named detachable handler.
- [x] Cancel/invalidate refresh/request observations during UI/runtime teardown.
- [x] Late OS/native permission completion cannot refresh destroyed UI or a new coordinator generation.
- [x] Denied/unavailable remains normal and non-blocking.

### Audio settings rollback

- [x] If an audio-setting commit reverts, reapply actual profile values to runtime audio sources.
- [x] Add focused coverage at the lowest viable layer and PlayMode source where needed.

Primary files:

- `ExpeditionController.cs`
- `UiComposer.cs`
- `FeedbackController.cs`
- `MotionPermissionCoordinator.cs`
- HUD/UI tests as appropriate

## 9. Repair persistence rollback graph fidelity

- [x] In `ProfileStateCopier`, preserve identity of surviving regions/buildings/producers while copying durable values.
- [x] Remove target-only building keys in surviving regions.
- [x] Remove target-only producer keys in surviving regions.
- [x] Audit other serialized nested maps for the same stale-key pattern; fix equivalent omissions required for exact graph equality.
- [x] Extend `SaveIntegrityApplicationTests.CopyInto_MatchesSerializedGraph_Exactly` or a companion test so the **target starts dirty** with extra nested keys and still serializes exactly like source after copy.
- [x] Keep the existing object-identity assertions green.

Do not replace the whole profile root to make the test pass.

## 10. Canonicalize dedup rebuild safely

- [x] Update `CreditedActivityKeys.Rebuild()` to remove null/empty values.
- [x] Collapse duplicates preserving most-recent occurrence order.
- [x] Apply capacity to the unique sequence, oldest first.
- [x] Rebuild membership exactly from the final entries.
- [x] Add tests for duplicates below capacity, duplicates across the eviction boundary, all-duplicate input, null/empty corruption, and over-capacity unique data.
- [x] Prove `TryMarkCredited` rejects every surviving credited key after rebuild/save/load.
- [x] Run existing exact-once activity/save/restart tests to ensure no regression.

No schema bump is expected for this repair unless the persisted shape changes.

## 11. Add ADR 0011 and reconcile documentation

- [x] Add `docs/adr/0011-...md` or the next available ADR number if main advanced.
- [x] Explain provider lifetime, operation ownership/cancellation, cancellation-vs-acknowledgment, generation invalidation, timeout race semantics, and start adoption.
- [x] Update `docs/TECHNICAL_ARCHITECTURE.md`.
- [x] Update `docs/MOBILE_ACTIVITY_INTEGRATION.md`.
- [x] Update `docs/ACTIVITY_REWARD_SYSTEM.md` where exact-once/lifecycle wording changed.
- [x] Update `docs/TESTING_AND_PERFORMANCE.md` with new regression coverage and honest verification status.
- [x] Update `docs/DATA_MODEL.md` only if persisted semantics/repair policy need clarification.
- [x] Update `docs/IMPLEMENTATION_STATUS.md` with an M8.5 matrix and exact fresh evidence.
- [x] Remove the current documentation mismatch that implies the 30-second drain ceiling itself guarantees no stranded claim.

## 12. Mandatory validation matrix

Run every available gate after implementation and record exact command/result.

- [x] `dotnet test verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj`
- [x] `./scripts/verify-domain.ps1` or repository-supported shell equivalent
- [x] `./scripts/verify-unity-static.ps1`
- [x] `./scripts/verify-release-hygiene.ps1`
- [x] `./scripts/Test-AgentGuards.ps1` and supported shell guard where environment permits
- [x] repository identity guard
- [x] `git diff --check`
- [x] Unity compile/EditMode/PlayMode if a licensed editor is genuinely available
- [x] Android build/device checks if Build Support + suitable device are genuinely available
- [x] iOS build/device checks only with macOS/Xcode/signing environment

Do not label unavailable Unity/device tiers as PASS. Record `UNVERIFIED — <reason>`.

### Required functional acceptance scenarios

- [x] F1 stale Android prepared delivery cannot mutate newer claim.
- [x] F2 repeated/null/unknown Android resolution is no-op.
- [x] F3 passive timeout/cancel leaves movement retryable and no open claim stranded.
- [x] F4 cancellation/completion race has exactly one terminal owner.
- [x] F5 fatal persistence transition shuts provider down before graph disposal.
- [x] F6 retry/start-over in same process constructs a clean provider with no leaked native listener/session.
- [x] F7 host destruction releases provider idempotently.
- [x] F8 provider start success + domain begin rejection explicitly aborts provider session.
- [x] F9 poll/stop/permission operations cannot retain dead UI/runtime ownership indefinitely.
- [x] F10 vehicle/debug completion uses shared coordinator and repairs failed-commit marker resurrection.
- [x] F11 stop fault/cancel/null result uses shared durable close/repair semantics.
- [x] F12 reverted/fatal Expedition shows no positive reward success copy/cue.
- [x] F13 committed Expedition still shows correct result and reward.
- [x] F14 failed audio setting commit reapplies reverted canonical audio values.
- [x] F15 dirty-target rollback copy serializes exactly like durable source and preserves surviving identities.
- [x] F16 corrupted duplicate dedup input canonicalizes safely and cannot reopen credited membership.
- [x] F17 existing reboot/process-death/exact-once activity scenarios remain green.
- [x] F18 passive movement still requires no GPS and no new sensitive permission was introduced.

## 13. Completion / handoff

- [x] Re-run repository-wide search for every activity-provider operation, resolve path, service rebuild, and direct completion sequence. Confirm no unsanctioned path remains.
- [x] Reconcile all code comments/docs with actual implementation.
- [x] Mark completed OpenSpec tasks only with evidence.
- [x] Change this change's status to COMPLETE only when all locally executable requirements are satisfied.
- [x] Replace `.agent/EXECUTION_PROMPT.md` status with COMPLETE and a detailed execution report: start SHA, branch, final SHA, defects fixed, design decisions, tests/gates, blocked external tiers, docs/ADR updates, and remaining real follow-up.
- [x] Commit with a detailed session report and push the campaign branch per repository workflow.

If all M8.5 correctness work closes without a new High blocker, explicitly recommend **Unity/device certification** as the next campaign rather than inventing Region 2 or another speculative hardening tranche.
