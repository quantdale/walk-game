# Execution Prompt — M8.5 Runtime Ownership & Rollback Fidelity

**Status:** COMPLETE
**Planned-From:** `main@616924fcbe61bc50a1c7f064b0fe6fe00fb185ba`  
**Canonical OpenSpec change:** `openspec/changes/m8.5-runtime-ownership-and-rollback-fidelity/`  
**Target implementation branch:** `agent/walk-game/m8.5-<session-id>`  
**Campaign type:** non-hardware hardening / provider lifecycle / async ownership / rollback fidelity  
**Priority:** High

## Mission

Pull current repository truth, reconcile this plan against any intervening commits, then execute the **entire M8.5 OpenSpec change in one coherent implementation campaign**.

M8.4 fixed the central process -> commit -> provider resolution ordering, but a whole-repository planner audit of current `main@616924fc` found remaining reachable integrity gaps around provider/native lifetime, cancellation/late-task ownership, Android delivery identity, secondary active-session transaction paths, durability-gated presentation, rollback graph fidelity, and corrupted dedup repair.

Do not treat this as a request to patch only the first listed defect. Read the canonical OpenSpec package, reproduce the required regressions, implement every normative requirement, run all available gates, update architecture/status evidence, and leave unavailable editor/device tiers honestly UNVERIFIED.

## Absolute repository boundary

This repository is **`quantdale/walk-game`**. It is NOT `quantdale/simple-walk-game`.

Before any mutation:

```bash
sh scripts/assert-repo-identity.sh
# or
./scripts/Assert-RepoIdentity.ps1
```

STOP on mismatch. Never import branch names, SHAs, prompts, code, or assumptions from the sibling repository.

## Authoritative change package

Read these files in full before editing:

1. `openspec/changes/m8.5-runtime-ownership-and-rollback-fidelity/audit.md`
2. `openspec/changes/m8.5-runtime-ownership-and-rollback-fidelity/proposal.md`
3. `openspec/changes/m8.5-runtime-ownership-and-rollback-fidelity/design.md`
4. `openspec/changes/m8.5-runtime-ownership-and-rollback-fidelity/specs/runtime-ownership/spec.md`
5. `openspec/changes/m8.5-runtime-ownership-and-rollback-fidelity/tasks.md`

Also follow `AGENTS.md`, `.agent/PLANNER_HANDOFF.md`, current implementation status/roadmap, architecture/data/activity/mobile/privacy/testing docs, ADR 0007, ADR 0009, ADR 0010, and any newer ADR present after pull.

If this adapter conflicts with the normative OpenSpec requirements, the OpenSpec spec/design/tasks win subject to repository-global instructions.

## Confirmed planner findings — seed evidence, not a substitute for your own audit

The planner enumerated the full repository tree, reconciled current main/history and open PR/issue state, and semantically audited the activity/provider/persistence/runtime boundary plus adjacent gameplay/UI/world systems.

At planning time:

- `main = 616924fcbe61bc50a1c7f064b0fe6fe00fb185ba`.
- M8.4 is COMPLETE and introduced `ActivityTransactionCoordinator` / ADR 0010.
- No open PRs/issues compete with this campaign.
- Last executor evidence is 185/185 standalone tests plus static/release gates; rerun them yourself and do not report that count as fresh baseline evidence.
- Unity compile/EditMode/PlayMode, Android real step-sensor/build tier, iOS/Xcode, and physical performance remain documented UNVERIFIED in the current environment.

Concrete defects found:

1. **Android stale prepared-delivery resolution:** the Android provider does not verify `deliveryId` against the currently open claim; stale A can resolve newer B.
2. **Ownerless late passive task:** ticker has a 12 s timeout and a 30 s hard drain, then explicitly admits a provider claim may stay open until process restart.
3. **No provider teardown contract:** `GameHost` can null/rebuild provider/service references without explicitly releasing Android monitoring or iOS live pedometer state.
4. **Unbounded Expedition tasks:** start/poll/stop task observation has no cancellation/generation lifetime.
5. **Start adoption leak:** provider start can succeed and domain `BeginExpedition` can fail without stopping the provider session.
6. **Second transaction path:** `UiComposer.VehicleSessionRoutine` manually performs stop -> trust -> process -> commit -> resolve and its fault path uses uncommitted abandonment, bypassing M8.4 coordinator repair.
7. **Rolled-back reward still displayed:** `ExpeditionController` builds positive `LastRewardMessage` from the processed result even when persistence reverted/fatal.
8. **Permission UI lifetime leak:** anonymous `StateChanged` subscription is not detached and permission tasks have no owner cancellation.
9. **Audio presentation rollback gap:** optimistic runtime audio changes are not explicitly reapplied after profile rollback.
10. **Incomplete rollback copier:** stale nested building/producer keys survive `ProfileStateCopier.CopyInto` inside a surviving region.
11. **Dedup rebuild corruption bug:** duplicate serialized keys can interact with capacity trimming so `_set` forgets a key still present in `entries`, reopening duplicate credit.

Treat these as minimum starting evidence. Re-audit all affected call sites after pulling current code and fix equivalent Critical/High issues you discover. Do not expand to unrelated product features.

## Required startup sequence

1. Prove repository identity.
2. Fetch remote state.
3. Record branch, HEAD, upstream, worktree status, `origin/main`, recent commits, open PRs/issues.
4. If `origin/main` advanced beyond the Planned-From SHA, inspect/reconcile every intervening change before editing; preserve equivalent fixes.
5. Acquire the repository writer lease. One writer = one branch = one worktree.
6. Create/use `agent/walk-game/m8.5-<session-id>` from current authoritative main.
7. Run all locally available baseline gates and record exact fresh results.
8. Read the complete OpenSpec package and repository-required docs.
9. Write failing regressions for the confirmed defects before the corresponding production fix wherever feasible.

## Implementation directive

Implement every checkbox and normative requirement in the OpenSpec change. The required end state is summarized below; the OpenSpec package contains the full acceptance details.

### A. Provider lifetime / shutdown

Create the smallest explicit, idempotent provider lifetime contract. It may use `IDisposable`, an explicit shutdown API, or equivalent, but all implementations must comply.

- GameHost tears the old provider down **before** dropping/replacing the service/profile graph.
- Android stops native step monitoring.
- iOS stops live pedometer updates and invalidates old callback ownership.
- Debug/Unavailable providers follow the same public contract.
- teardown never fabricates durable acknowledgment.
- repeated teardown is harmless.

Cover fatal blocked transition, retry-load, start-over, and host destruction.

### B. Owned/cancelable provider operations

Give passive prepare, capability/permission, active start/poll/stop operations explicit owner lifetime. Prefer `CancellationToken` propagation if practical under the pinned Unity/C# runtime; an engine-free generation/lease abstraction is acceptable if it proves the same behavior.

The critical invariant is exactly-one terminal ownership when timeout/cancellation races completion. Do not solve this by simply increasing timeouts.

### C. Android claim identity

Move claim identity into the engine-free reconciler/state machine so the real semantics are headlessly certifiable. Prepared delivery resolution must mutate only the matching current claim. Stale, repeated, null, and unknown IDs are no-ops. Failed-commit movement remains exactly-once retryable.

### D. Passive timeout cleanup

Remove the current “hard deadline then nobody owns the future result” behavior. A timed-out/abandoned operation must either be truly canceled before it can create provider-private claim state or keep a deterministic cleanup owner for any late result. No stranded claim, no cursor/reward advancement without commit, no loss of retryable movement.

### E. Active-session start/poll/stop ownership

- provider start success + domain adoption failure -> explicitly stop/abort provider session;
- poll/stop cannot retain dead controller/runtime ownership indefinitely;
- old-generation late samples/results are harmless;
- stop fault/cancel/null uses the shared no-result completion/cleanup path.

### F. One completion transaction path

Make all normal/debug/vehicle active-session completions use the sanctioned coordinator protocol for process -> commit -> provider resolve -> rollback-marker repair. Remove duplicate manual transaction sequencing.

After implementation, repository-wide search must prove no unsanctioned completion sequence remains.

### G. Durability-gated player truth

- committed Expedition -> positive reward summary/success feedback allowed;
- reverted Expedition -> positive reward summary/cue cleared; truthful unsaved/retryability copy only;
- fatal -> recovery copy only;
- start success cue only after actual provider + domain adoption;
- failed audio-setting commit reapplies reverted canonical audio values;
- permission event/task lifetime is detached/cancelled on UI teardown.

### H. Exact rollback graph

Fix `ProfileStateCopier` so target-only nested building/producer keys are removed while surviving graph-node identities remain stable. Add a dirty-target serialized-graph fidelity test.

Audit equivalent nested-map retention while there; fix only correctness-equivalent omissions required for exact disk truth.

### I. Dedup canonicalization

Repair `CreditedActivityKeys.Rebuild()`:

- remove null/empty;
- collapse duplicate keys preserving most-recent occurrence order;
- apply capacity to unique entries;
- rebuild membership exactly from final entries.

Add corruption/eviction/save-load tests proving a surviving credited key cannot reopen.

### J. ADR/docs/status

Add ADR 0011 (or next free number) defining provider instance lifetime and async operation ownership, including cancellation-vs-durable-ack semantics and timeout/completion races.

Update architecture, mobile integration, activity/reward, testing/performance, implementation status, and data-model docs if required. Remove the current docs/code mismatch around the 30-second late-drain guarantee.

## Mandatory acceptance scenarios

Do not mark the campaign complete until the OpenSpec functional matrix is satisfied. At minimum prove:

- stale Android claim A cannot resolve current claim B;
- null/repeated/unknown delivery IDs are no-op;
- passive timeout/cancel cannot strand a claim and movement stays retryable;
- cancellation/completion race has exactly one terminal owner;
- fatal/retry/start-over/destruction paths release provider correctly;
- same-process replacement cannot retain old iOS live-session or duplicate Android listener state;
- start success + domain rejection aborts provider session;
- old/hung poll/stop/permission work cannot mutate destroyed/new runtime;
- vehicle/debug path uses shared coordinator and failed-save marker repair;
- stop fault/null path uses shared durable close/repair;
- reverted/fatal Expedition shows no positive reward success state;
- committed Expedition still shows correct reward;
- audio runtime reverts with canonical settings after failed commit;
- dirty-target rollback copy equals durable source serialization exactly;
- duplicate/corrupt dedup entries rebuild safely across capacity;
- existing reboot/process-death/exact-once tests stay green;
- passive earning still has no GPS/new health permission dependency.

## Required validation

Run every available repository gate and record commands/results, including:

```text
dotnet test verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj
scripts/verify-domain.ps1
scripts/verify-unity-static.ps1
scripts/verify-release-hygiene.ps1
scripts/Test-AgentGuards.ps1
repository identity guard
git diff --check
```

Use repository-supported shell equivalents where applicable.

Run Unity compile/EditMode/PlayMode only if a licensed editor session is genuinely available. Run Android build/device only if Android Build Support and suitable hardware are genuinely available. Run iOS only with macOS/Xcode/signing. Do not bypass those prerequisites and do not convert unavailable tiers into PASS.

## Scope exclusions

Do NOT implement:

- Region 2;
- HealthKit / Health Connect;
- active GPS/location feature expansion;
- cloud/accounts/social/multiplayer;
- reward rebalance unrelated to correctness;
- art overhaul;
- Addressables migration;
- speculative performance optimization without measured hardware evidence.

If you uncover an unrelated low/medium idea, document it for later rather than derailing M8.5. Fix newly discovered Critical/High correctness defects and Medium state-integrity defects necessary for M8.5 convergence.

## Completion protocol

At the end of the execution session:

1. Re-run all available gates from a clean-enough final state.
2. Re-run repo-wide searches for activity completion, provider resolution, provider lifetime, and service rebuild call sites.
3. Update OpenSpec tasks/status with evidence.
4. Update `docs/IMPLEMENTATION_STATUS.md` with exact final matrix and remaining UNVERIFIED external tiers.
5. Change this file to `Status: COMPLETE` and replace the body or append a detailed final report containing:
   - start SHA and reconciled base;
   - campaign branch/worktree/lease;
   - final SHA(s);
   - every Critical/High/required Medium defect fixed;
   - architecture/ADR decisions;
   - exact test/static/editor/device evidence;
   - blocked/unverified tiers and why;
   - documentation changed;
   - any remaining real blocker/follow-up.
6. Commit with a detailed session-report commit message and push the campaign branch according to repository workflow.

If M8.5 closes without a new High blocker, recommend **real Unity/device certification / M8 Device Ready validation** as the next campaign. Do not manufacture another headless hardening cycle merely to keep working.

---

## Executor Report — M8.5 COMPLETE

**Campaign branch:** `agent/walk-game/m8.5-exec-20260826`
**Start SHA:** `fb619e93df3db2c5b86a190a6dcea01efb64442f` (planner handoff reconciled from `main@616924fcbe61bc50a1c7f064b0fe6fe00fb185ba`; the only intervening commit was the OpenSpec staging commit itself — no code drift)
**Final SHA:** see commit `feat(activity): M8.5 runtime ownership & rollback fidelity (ADR 0011)`
**Lease:** `sess-m85-exec-20260826` on `Nayeon_16`, acquired before first mutation, start SHA recorded

### Reconciliation

`origin/main` had advanced one commit beyond Planned-From (`616924fc` → `fb619e9`, the planner's own staging commit). Diff inspected: documentation-only (OpenSpec package + this prompt rewrite). No landed code fixes to preserve; no equivalent work redone.

### Defects fixed — every confirmed finding reproduced and repaired

All 11 planner findings (H1–H9, M1–M2) were verified in source before editing. The two pure-behavioral regressions were written FIRST and failed on pre-fix code (6/8 new assertions failing), then fixed:

1. **H1 stale Android claim resolution** — engine-free `AndroidCounterReconciler` claims now carry identity (`OpenClaimId`); `AcknowledgeClaim(id)`/`RestoreClaim(id)` resolve only the named open claim; the adapter binds `PreparedActivityDelivery.deliveryId` to it. Stale A cannot ack/restore newer B; repeated/null/unknown are no-ops; failed-commit retry stays exactly-once.
2. **H2 ownerless late passive task** — replaced "12 s + 30 s hard drain then admit stranding" with engine-free `OperationLease` (atomic exactly-one-terminal-owner) + `ProviderOperations.AbandonPreparation`: ownership transfers to a cleanup continuation that survives the coroutine and rejects any late delivery unprocessed (`durable=false`, cursor untouched). No cutoff exists after which a completion becomes ownerless.
3. **H3 no provider teardown contract** — `IActivityProvider.Shutdown()`: idempotent; refuses new native work; stops Android monitoring / iOS live updates; RESTORES claim/completion state instead of consuming; never fabricates durable acknowledgment. `GameHost.ShutdownProvider()` runs BEFORE graph drop in `EnterBlockedState`, `RetryLoadFromDisk`, `StartOverWithFreshProfile`, `OnDestroy`.
4. **H4 unbounded Expedition tasks** — bounded policy waits (start/poll 10 s, stop 30 s) with lease-owned late-result disposal; hung stop routes through the shared no-result durable close plus non-durable late resolution.
5. **H5 start adoption leak** — `ActiveSessionAbort.Abort(provider)`: stop + non-durable resolve returns base movement to the passive stream; wired into ExpeditionController, ticker debug path, vehicle path, and controller OnDestroy.
6. **H6 second transaction path** — `UiComposer.VehicleSessionRoutine` now delegates result/fault/no-result to `ActivityTransactionCoordinator` (its trust facts ride along as parameters). Repository-wide search confirms production completion sequencing exists ONLY inside the coordinator (+ its sanctioned cleanup owners) plus boot recovery.
7. **H7 rolled-back reward displayed as earned** — engine-free `ExpeditionResultPresentation`: positive reward copy only for committed outcomes; reverted shows truthful unsaved/retryable copy; fatal shows recovery copy only. Start cue moved behind `ExpeditionController.StartConfirmed` (fires only after real provider start + domain adoption).
8. **M1 permission UI leak** — named detachable handler; refresh/request observations bounded with `DiscardLateResult` owners; late native completions cannot fire into destroyed UI.
9. **M2 audio divergence** — `_feedback.ReapplyCanonicalSettings()` on `PersistenceReverted`.
10. **H8 rollback graph fidelity** — `ProfileStateCopier.CopyWorldState` prunes target-only nested building/producer keys inside surviving regions AFTER reusing surviving instances; dirty-target test proves exact serialized equality plus stable identities.
11. **H9 dedup corruption** — `CreditedActivityKeys.Rebuild()` canonicalization: remove null/empty, collapse duplicates most-recent-first, apply capacity to unique entries, rebuild membership exactly; compaction can never reopen a surviving credited key.

No new Critical/High defects discovered during the executor audit beyond the seeded list; scope stayed within M8.5.

### Architecture decisions (ADR 0011)

- Provider lifetime = explicit idempotent `Shutdown()` (not IDisposable-only, so the contract is named and greppable at call sites).
- Operation ownership = smallest reusable primitive: `OperationLease` CAS token + three explicit cleanup-owner helpers (`AbandonPreparation`, `AbandonSessionStop`, `DiscardLateResult`) rather than a general async framework or CancellationToken plumbing through JNI/CoreMotion bridges.
- Cancellation is never acknowledgment: abandoned operations converge provider-private state to retryable form; cursors/rewards move only inside the sanctioned transaction.
- Presentation policy extracted engine-free (`ExpeditionResultPresentation`) so durability gating is headlessly certifiable.

### Tests added/changed

- New: `OperationOwnershipTests` (7), `ProviderLifetimeTests` (5), `RuntimeOwnershipOrchestrationTests` (8 incl. F10/F11/F12/F13 presentation), `DedupCanonicalizationTests` (8).
- Extended: `AndroidCounterReconciliationTests` (identity-bound claim suite: 4 new + 4 rewritten), `SaveIntegrityApplicationTests` (+1 dirty-target fidelity), `PermissionFlowTests` (fakes migrated to new interface).
- Totals: 185 → **213/213** headless passing. All existing exactly-once suites (`MovementDeliveryDurabilityTests`, `ActivityServiceTests`, `SaveLoadTests`, `InterruptedSessionRecoveryTests`) remain green.

### Exact validation results (this campaign, fresh)

- Baseline pre-change: `dotnet test` **185/185 PASS**.
- Post-change: `dotnet test verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj` — **213/213 PASS** (~2 s, net8.0).
- `scripts/verify-domain.ps1` — PASS (exit 0, restore check + full suite).
- `scripts/verify-unity-static.ps1` — PASS (107 assets / 107 metas, Unity 6000.3.4f1 pin, package invariants, Bootstrap scene).
- `scripts/verify-release-hygiene.ps1` — PASS (63 runtime sources scanned, manifest minimal).
- `pwsh scripts/Test-AgentGuards.ps1` — ps tier **24/24 PASS**; 12 sh-tier failures are the known WSL `bash.exe` shadowing in this environment (identical matrix to M8.3/M8.4 records; ps tier authoritative).
- `sh scripts/assert-repo-identity.sh` — PASS pre-work (`quantdale/walk-game`); re-run before integration.
- `git diff --check` — clean.

### Editor/device UNVERIFIED evidence

- Unity 6000.3.4f1 installed but Hub holds zero accounts; licensing client reports "Token not found in cache", 0 entitlement groups, no ULF. Repro chain: sign into Hub → activate → `scripts/setup-unity-project.ps1` → `verify-unity-editmode.ps1` / `verify-unity-playmode.ps1`. New Unity wiring (`ActivityTicker`/`ExpeditionController`/`UiComposer`/providers) compiles under static gates only; PlayMode remains UNVERIFIED by construction.
- Android Build Support absent (`AndroidPlayer` missing; prior install attempt `ELEVATION_CANCELLED`). Repro: install Build Support + SDK → `verify-android-smoke.ps1`. UNVERIFIED.
- iOS/macOS/Xcode absent. iOS teardown change statically reviewed only. UNVERIFIED.

### Documentation changed

- NEW `docs/adr/0011-runtime-ownership-provider-lifetime.md` (provider lifetime, operation ownership, cancellation-vs-ack, timeout/completion race rule, claim identity, start adoption, presentation truth).
- `docs/adr/0010-runtime-orchestration-durability.md` — amendment note marking the hard-drain paragraph historical (superseded guarantee is stronger and now real).
- `docs/TECHNICAL_ARCHITECTURE.md` — coordinator section extended with operation-ownership contract; 30 s mismatch removed.
- `docs/MOBILE_ACTIVITY_INTEGRATION.md` — late-delivery guarantee restated per actual ownership semantics; provider shutdown documented.
- `docs/ACTIVITY_REWARD_SYSTEM.md` §16 — one-completion-path, timeout-ownership, claim identity, and player-truth rules added.
- `docs/TESTING_AND_PERFORMANCE.md` — refreshed evidence (213/213, 107/107, new suites enumerated).
- `docs/DATA_MODEL.md` — dedup repair policy note (no schema change).
- `docs/IMPLEMENTATION_STATUS.md` — M8.5 campaign section with defect/fix/evidence table and certification matrix.
- OpenSpec change package flipped to COMPLETE with evidence footer in `tasks.md`.

### Remaining follow-up

None blocking. Per the exit direction: if no new High blocker surfaces in review, the next campaign should be **real Unity/device certification (M8 Device Ready validation)** — licensed editor EditMode/PlayMode runs, Android build + step-sensor smoke, iOS/Xcode build — rather than another speculative headless tranche.
