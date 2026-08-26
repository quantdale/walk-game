# Runtime Ownership & Rollback Fidelity Specification

**Change:** `m8.5-runtime-ownership-and-rollback-fidelity`  
**Status:** COMPLETE
**Scope:** provider lifetime, async operation ownership, activity transaction convergence, durability-gated presentation, rollback graph fidelity, dedup repair

The terms MUST, MUST NOT, SHOULD, and MAY are normative.

## ADDED Requirements

### Requirement: Provider instances have explicit runtime lifetime

Every `IActivityProvider` implementation MUST participate in an explicit, idempotent provider-lifetime contract. Dropping the last managed reference MUST NOT be the mechanism used to stop native monitoring or active-session state.

#### Scenario: host enters fail-closed persistence state

Given a gameplay runtime owns a live provider, when fatal persistence causes the host to tear down the playable service graph, then the provider MUST be shut down before the host drops/replaces provider and profile references, and shutdown MUST NOT acknowledge uncommitted movement as durable.

#### Scenario: same-process retry or start-over

Given an old provider was composed and the player recovers/restarts the runtime without killing the process, when a replacement provider is created, then the old provider MUST already have released native listeners/live sessions so the replacement does not inherit duplicate monitoring or an `AlreadyRunning` state caused by leakage.

#### Scenario: repeated shutdown

Given a provider has already been shut down, when shutdown is invoked again by another lifecycle path, then it MUST be harmless and MUST NOT double-resolve claims or throw because native state is already stopped.

### Requirement: Provider operations have one terminal owner

Every asynchronous provider operation used by the application MUST have an explicit owner/generation and deterministic cancellation or invalidation semantics.

#### Scenario: completion wins cancellation race

Given an operation is completing while owner cancellation is requested, when completion atomically wins terminal ownership, then the current owner MAY process the result exactly once and the cancellation path MUST NOT also abandon/resolve it.

#### Scenario: cancellation wins completion race

Given cancellation/invalidation wins terminal ownership, when the underlying task later completes, then its result MUST NOT mutate canonical state or a newer runtime; any provider-private staged state MUST still reach the provider's safe abandon/restore cleanup path exactly once.

#### Scenario: old runtime generation completes late

Given provider generation A has been disposed and generation B is active, when an A operation completes late, then it MUST NOT update B's UI, profile, cursor, provider claim, permission state, or session state.

### Requirement: Cancellation never implies durability

Cancellation, timeout, teardown, or owner destruction MUST NOT be treated as evidence that activity was durably committed.

#### Scenario: prepared movement is canceled before commit

Given passive movement has been staged but not durably committed, when the operation is canceled or owner teardown begins, then that movement MUST remain retryable through provider restore/abandon semantics or process-restart reconstruction.

### Requirement: Passive reconciliation cannot orphan a provider claim

A passive prepare operation MAY use a scheduling timeout, but the application MUST NOT reach a state where a future completion can create/return a claim and no owner exists to process or safely reject it.

#### Scenario: provider preparation exceeds soft timeout

Given `PreparePassiveDeliveryAsync` exceeds the scheduling timeout, when the current reconcile attempt stops waiting, then the ownership/provider contract MUST guarantee the eventual operation cannot strand an open claim; cursor/reward state MUST remain unchanged unless a delivery is subsequently adopted and committed.

#### Scenario: timeout and delivery completion race

Given timeout/cancellation and prepared-delivery completion occur concurrently, then exactly one terminal path MUST own the delivery and the provider MUST observe at most one final resolution/abandon action for that delivery.

#### Scenario: next reconcile after timeout

Given a timed-out reconcile did not durably commit movement, when the next reconcile starts, then prior provider-private state MUST NOT permanently block preparation and the same uncommitted movement MUST remain eligible for exactly-once retry.

### Requirement: Android prepared-delivery resolution is claim-identity bound

The Android cumulative-counter claim state MUST have an explicit identity that is carried into `PreparedActivityDelivery` and required for acknowledgment/restoration.

#### Scenario: stale acknowledgment

Given claim A was closed and claim B is currently open, when a stale durable resolution for A arrives, then B MUST remain open and unchanged.

#### Scenario: stale rejection

Given claim A was closed and claim B is currently open, when a stale non-durable resolution for A arrives, then B MUST remain open and pending steps MUST NOT be duplicated/restored incorrectly.

#### Scenario: repeated resolution

Given claim A was already acknowledged or restored, when A is resolved again, then the operation MUST be a no-op.

#### Scenario: null or unknown identity

Given no matching open claim exists, when a null/unknown delivery identity is resolved, then reconciler state MUST remain unchanged.

#### Scenario: failed commit retry

Given Android claim A was prepared and its application commit reverted, when A is rejected non-durably, then its movement MUST be returned to the pending stream exactly once and a later claim/retry MAY commit it once.

### Requirement: Provider session start must be adopted or aborted

A provider-side active session MUST NOT remain running if the domain fails to adopt it.

#### Scenario: provider start succeeds but domain begin fails

Given `StartSessionAsync` succeeds, when `ActivityService.BeginExpedition` rejects because another canonical session owns the window or another invariant fails, then the provider session MUST be explicitly stopped/aborted and the application MUST return to an idle session state without reward.

### Requirement: Active-session operations are owner-bounded

Start, poll, and stop operations MUST NOT retain live runtime ownership indefinitely after controller destruction, provider replacement, or fail-closed transition.

#### Scenario: poll hangs

Given a provider poll never completes, when its owner is canceled/destroyed or policy timeout is reached, then the application MUST regain control without blocking the Unity main loop; a later poll completion MUST be harmless.

#### Scenario: stop hangs or faults

Given stop is canceled, faults, or returns no result, then the application MUST use the shared no-result completion/cleanup protocol to close/repair canonical suppression state while preserving uncommitted base movement retryability.

### Requirement: One active-session transaction protocol owns all completions

Normal Expedition completion, debug simulated sessions, vehicle-like debug sessions, and no-result/fault paths MUST NOT duplicate the process -> persistence -> provider-resolution sequence independently.

#### Scenario: vehicle debug session commit fails

Given the debug vehicle session produces a result and the profile commit reverts to a durable active-session marker, when completion resolves, then the same shared coordinator protocol used by normal Expeditions MUST reject the provider completion, repair the resurrected marker, and leave base movement retryable in the same process.

#### Scenario: no-result debug session

Given a debug/native session ends without a usable result, when completion is handled, then canonical active-session cleanup MUST go through the same sanctioned durable close/repair path rather than an uncommitted `AbandonExpedition` shortcut.

#### Scenario: repository search after implementation

Given M8.5 is complete, when direct calls to `ProcessSessionResult`, `ResolveSessionCompletion`, `AbandonExpedition`, and `CommitChanges*` are audited, then no unsanctioned application completion sequence may remain outside the documented coordinator boundary.

### Requirement: Motion permission operations respect UI/runtime lifetime

Permission refresh/request operations MUST be cancelable or generation-invalidated by their owner, and event subscriptions MUST be detachable.

#### Scenario: UI destroyed during permission request

Given a native permission round is outstanding, when `UiComposer` is destroyed or runtime generation changes, then its state-change handler MUST be detached and a later completion MUST NOT invoke destroyed UI or mutate a newer coordinator generation.

#### Scenario: denied permission

Given the user denies motion permission, when the request resolves under the current owner, then denial MUST remain a normal non-blocking state and Builder/Explore gameplay MUST remain available.

### Requirement: Player reward presentation is durability-gated

Positive reward copy and success-only completion feedback MUST correspond to state that was durably committed.

#### Scenario: committed Expedition

Given a session result is processed and persistence returns `Committed`, when the Expedition UI refreshes, then it MUST show the correct committed `+steps -> +Vitality` summary and MAY play success-only completion feedback.

#### Scenario: reverted Expedition

Given the same session result is processed but persistence returns `RevertedToLastKnownGood`, when the UI refreshes, then no positive Vitality reward summary or success-only completion cue may remain; the UI MUST instead explain that the Expedition could not be saved and that retryable movement remains safe according to the provider contract.

#### Scenario: fatal persistence loss

Given completion triggers `FatalPersistenceLoss`, then the runtime MUST show recovery-state messaging only and MUST NOT present the processed-but-uncommitted reward as earned.

### Requirement: Expedition start feedback reflects actual start

Success-like Expedition-start feedback MUST occur only after both provider start and canonical domain session adoption succeed.

#### Scenario: provider start denied

Given a user taps Start but motion permission is denied or the provider is unavailable, then the application MUST NOT play the same success cue used for an active Expedition.

### Requirement: Runtime-applied audio settings converge after rollback

Optimistically applied audio settings MUST be reapplied from canonical profile state after persistence rollback.

#### Scenario: volume change fails to save

Given master/music/effects volume is changed in UI and applied to the runtime audio source, when the commit reverts to the prior durable profile values, then the actual audio source gain/state MUST be refreshed to those reverted values before the UI continues to claim the old setting.

### Requirement: In-place rollback removes stale nested state

`ProfileStateCopier.CopyInto` MUST make serializer-visible target state equal to source durable state while preserving required object identity for surviving graph nodes.

#### Scenario: target contains extra building key

Given source region R has buildings A/B and target region R has A/B/X, when source is copied into target, then A/B surviving instances MUST retain their required identities and X MUST be removed.

#### Scenario: target contains extra producer key

Given source region R has producer P and target region R has P/Q, when copied, then P MUST retain its surviving identity/value mapping and Q MUST be removed.

#### Scenario: exact serialized graph

Given a deliberately dirty target has extra nested serializer-visible data, when `CopyInto` completes, then serializing target MUST equal serializing the repaired source exactly.

### Requirement: Dedup rebuild produces canonical unique bounded membership

`CreditedActivityKeys.Rebuild` MUST remove invalid entries, collapse duplicate keys by most-recent occurrence, apply capacity to the unique ordered sequence, and make membership exactly equal to final entries.

#### Scenario: duplicate before capacity trimming

Given entries `A, B, A, C` and sufficient capacity, when rebuilt, then the canonical order MUST represent the most recent occurrence of A and contain A exactly once.

#### Scenario: duplicate crosses eviction boundary

Given capacity 3 and entries `A, B, A, C, D`, when rebuilt, then the result MUST be `A, C, D` and membership MUST contain exactly A/C/D; A MUST NOT be reopened merely because its older duplicate was evicted.

#### Scenario: invalid entries

Given null/empty entries and valid keys, when rebuilt, then invalid entries MUST be removed without affecting membership of valid keys.

#### Scenario: save/load replay

Given a canonical dedup list is serialized and reloaded, when the same surviving key is presented to `TryMarkCredited`, then it MUST be rejected as already credited.

### Requirement: Provider teardown preserves privacy scope

M8.5 MUST NOT introduce GPS or new health-platform permission requirements for passive movement.

#### Scenario: passive step earning after M8.5

Given location permission is absent, when motion permission and a supported phone step source are available, then passive movement processing MUST remain architecturally valid without GPS.

## MODIFIED Requirements

### Requirement: M8.4 activity transaction durability includes lifecycle ownership

The ADR 0010 transaction ordering remains authoritative for process -> commit -> resolve -> rollback-marker repair, but it MUST be extended by ADR 0011 (or successor) so the provider delivery/session and the asynchronous operation that produced it remain owned through their terminal outcome.

#### Scenario: fatal runtime replacement during outstanding provider operation

Given an operation is in flight and persistence transitions to blocked recovery, then provider teardown/operation invalidation MUST prevent that old operation from later bypassing the M8.4 transaction boundary or mutating the replacement runtime.

### Requirement: Documentation must state only proven guarantees

Architecture, mobile-integration, activity/reward, testing, and implementation-status documentation MUST match implemented lifecycle semantics and actual verification evidence.

#### Scenario: unavailable editor/device tier

Given Unity licensing, Android build support/hardware, or macOS/Xcode is unavailable, when M8.5 is reported complete, then those tiers MUST be recorded as `UNVERIFIED` with the reason rather than inferred from headless/static success.

#### Scenario: late passive delivery guarantee

Given M8.5 replaces the current hard-drain behavior, when documentation describes late delivery handling, then it MUST describe the actual ownership/cancellation guarantee and MUST NOT imply that an arbitrary timeout alone prevents stranded claims.
