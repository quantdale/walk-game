# Execution Prompt — M8.1 Save Integrity & Persistence Failure Containment

**Status:** COMPLETE  
**Planned-From:** `main@0cdf823755466a604967fe88ac4913b345d5d294`  
**Target branch:** `main`  
**Campaign type:** non-hardware hardening / state-integrity correction  
**Priority:** Critical — player-progress preservation  

## Mission

Execute one coherent hardening campaign that makes local persistence **fail closed** across boot, recovery, gameplay mutation, autosave, and shutdown. A corrupt, unreadable, incompatible, or otherwise unpersistable player save must never be silently converted into a new writable profile, overwritten by lifecycle autosave, or presented as a normal playable session whose mutations can erase the last recoverable state.

This is the highest-value unblocked campaign after M8 because the repository's own priority rule places **state integrity first**, while the remaining editor/device certification gates are externally blocked by Unity entitlement, Android Build Support/elevation, macOS/Xcode, or suitable physical hardware.

Do not turn this into a narrow patch to one `if` statement. Audit the complete codebase impact and establish a coherent persistence-health contract used by every state-changing path.

## Why this campaign exists — confirmed planner findings

The planner audited current code/tests/docs and found a mismatch between documented guarantees and application behavior:

1. `FileSaveRepository.TryLoad` distinguishes these outcomes:
   - `Empty` — no save exists;
   - `RecoveredFromBackup` — backup loaded successfully;
   - `Failed` — save material exists but cannot be loaded;
   - `IncompatibleSchema` — save cannot safely migrate.
2. `GameHost.Boot()` currently uses `bool fresh = !_repository.TryLoad(...)` and calls `NewProfile()` for **every false result**, not only `Empty`.
3. Therefore a corrupt-main+corrupt-backup or future-schema save can boot into a brand-new in-memory profile even though the repository deliberately preserved the failed files.
4. `UiComposer.GetNextGoal()` explicitly tells `Failed` / `IncompatibleSchema` users that the current session remains playable.
5. Multiple state-changing application paths call `host.Persist()` but do not make the mutation's success conditional on persistence success. This can allow a failed-load session or later write failure to diverge from the last-known-good disk state.
6. Lifecycle hooks (`OnApplicationFocus`, `OnApplicationPause`, `OnDestroy`) automatically call `Persist()`. Without a write-safety gate, a fatal boot recovery state can later overwrite the failed slot merely because the app backgrounds or closes.
7. Existing `SaveLoadTests` correctly prove that the **repository alone** does not delete incompatible/corrupt files, but current PlayMode certification does not cover fatal-load boot behavior through `GameHost`.
8. There is an additional recovery-rotation hazard to prove or eliminate: after loading from `.bak` because the main file is corrupt, a subsequent normal `Save()` currently sees the corrupt main, copies it over `.bak`, deletes main, then moves the new temp into place. If that recovery save is interrupted after the trusted backup is overwritten, the last-known-good backup can be lost. The campaign must validate the exact behavior with fault injection and redesign it if necessary.
9. `TECHNICAL_ARCHITECTURE.md` requires atomic progression transactions and says persistence failure must roll back the in-memory transaction or reload last-known-good state. Current application mutation paths do not yet enforce that contract uniformly.

Treat these as starting findings, not the complete defect list. Re-audit the entire repository before editing.

## Required reading and repository reconciliation

Before implementation:

1. Read `AGENTS.md` and `.agent/PLANNER_HANDOFF.md`.
2. Read, in repository-required order:
   - `docs/MASTER_PLAN.md`
   - `docs/ROADMAP.md`
   - `docs/TECHNICAL_ARCHITECTURE.md`
   - `docs/DATA_MODEL.md`
   - `docs/AGENT_EXECUTION_GUIDE.md`
   - `docs/IMPLEMENTATION_STATUS.md`
   - persistence/testing-related sections of `docs/TESTING_AND_PERFORMANCE.md` and any relevant ADRs.
3. Reconcile Git before modifying anything:
   - inspect `git status`, branch, upstream, and current HEAD;
   - fetch/pull/rebase only as required by repository policy;
   - if this prompt was planned from an older commit because work landed after planning, inspect the intervening commits/diff first and preserve already-landed equivalent fixes rather than redoing them.
4. Inspect useful open issues/PRs if any exist. Do not assume there are none because the planner saw none at planning time.
5. Run the currently available baseline gates before implementation where the environment permits them. Record exact command/result evidence rather than copying old counts.

## Workstream A — exhaustive persistence and mutation audit

Build a complete map of the persistence lifecycle before choosing the final design. At minimum inspect:

- `Assets/WalkGame/App/GameHost.cs`
- `Assets/WalkGame/Persistence/*`
- `Assets/WalkGame/App/UiComposer.cs`
- `Assets/WalkGame/App/AppFlowController.cs`
- `Assets/WalkGame/App/ActivityTicker.cs`
- `Assets/WalkGame/App/ExpeditionController.cs`
- `Assets/WalkGame/UI/ProjectPanelController.cs`
- all gameplay/domain services that mutate `PlayerProfile`, `WorldState`, `RegionState`, currency/resources, placement, activity cursors/dedup, settings, lore, onboarding, production checkpoints, and sessions
- all tests that directly or indirectly assert save/load/restart behavior.

Search the **entire repository**, not only changed files, for:

- `Persist(` / `Save(` / `TryLoad(` / `DeleteAll(`;
- all direct or indirect mutations of canonical profile state;
- lifecycle autosave hooks;
- user-visible copy tied to save health;
- debug/reset utilities;
- event publication that can occur before durable commit;
- any code that assumes a non-null profile means persistence is healthy.

Produce the implementation from the complete mutation-to-durability map. Do not leave one mutation path capable of bypassing the new safety contract simply because it was outside the original finding.

## Workstream B — explicit boot/persistence health state

Establish one authoritative application-level representation of persistence health. Exact naming/design is flexible, but semantics are not.

Required behavior:

1. **Only `SaveLoadResult.Empty` may automatically create a fresh profile.**
2. `Success` boots the loaded profile normally.
3. `RecoveredFromBackup` boots the recovered profile but retains enough repository state to protect that trusted recovery source until a new save is durably established.
4. `Failed` and `IncompatibleSchema` must enter a fail-closed recovery state:
   - do not silently manufacture a normal writable player profile;
   - do not run normal progression systems against a throwaway profile and later autosave it over the failed slot;
   - do not process movement rewards, idle production, restoration, building placement, lore, collection, settings mutations, onboarding progression, or any other durable gameplay mutation as though persistence were healthy;
   - lifecycle focus/pause/destroy hooks must not overwrite the failed slot.
5. Preserve failed source material byte-for-byte unless the player takes an explicit destructive recovery action. If implementing quarantine/archival, make it deterministic and testable; never simply delete the failed main/backup.
6. Do not weaken forward-schema safety. A save from a newer schema must remain untouched and must never be rewritten by an older build.
7. Avoid a global grab-bag singleton or UI-owned persistence authority. Keep the design aligned with the current composition-root/repository architecture.

Prefer a small explicit state machine/value object over scattered booleans such as `disableSaving` at unrelated call sites.

## Workstream C — trusted-backup recovery and atomic file rotation

Red-team `FileSaveRepository` itself, especially the first save after `RecoveredFromBackup`.

The invariant is stronger than "the final successful save looks correct":

> At every injected interruption point, at least one trustworthy copy of the last-known-good profile must survive until a newer profile is fully validated and durable.

Required cases to design/test:

- corrupt main + valid backup -> recovery;
- recovered profile -> successful next save;
- recovered profile -> failure while writing temp;
- failure copying/rotating backup;
- failure after main removal but before replacement;
- failure moving the validated temp into place;
- incompatible main with absent/invalid backup;
- main absent + valid backup;
- stale temp files from an interrupted previous save;
- first-ever save with no main/backup.

If the current three-file rotation algorithm cannot satisfy the invariant after backup recovery, change the algorithm or track trusted-slot state explicitly. Do not solve it by deleting evidence first.

Any change to filesystem sequencing must keep deterministic fault-injection seams and tests.

## Workstream D — transactional application mutations

Bring actual application behavior into line with the architecture's transaction principle.

For every player-visible durable mutation, define what happens when persistence succeeds and what happens when it fails. The user must never be told a permanent action succeeded while the only durable state is still the previous profile.

Audit and harden at least these categories:

- restoration project completion and Vitality/resource spending;
- building placement confirmation;
- production collection and checkpointing;
- lore discovery;
- passive activity reward/cursor/dedup updates;
- Expedition completion/abandon/recovery transitions;
- milestone grants;
- settings and onboarding changes;
- debug mutations where allowed;
- any future-safe generic mutation pathway introduced by the campaign.

Choose one coherent approach, for example an application persistence coordinator that snapshots/commits/restores state, or another design that satisfies the same invariants. Do not sprinkle ignored `if (!Persist())` checks everywhere.

On a persistence failure after an in-memory mutation, the system must do one of the architecture-approved behaviors:

- roll the mutation back to the exact prior canonical state; or
- reload/rebind the exact last-known-good state safely.

Whichever design you choose must also account for domain events/UI feedback. A failed transaction must not leave stale success-only presentation, audio/haptic celebration, or progression unlocks as though the write were durable.

Avoid serializing the profile twice in every hot path if a cleaner coordination mechanism can retain correctness. Correctness comes first; optimize only from evidence.

## Workstream E — player-facing recovery UX

Replace misleading save-failure copy with an explicit, understandable recovery state.

Minimum behavior:

- distinguish normal, recovered-from-backup, and blocked/fatal persistence states;
- when fatal, communicate that existing progress data was detected but could not be safely loaded;
- prevent normal progression actions while blocked;
- never advise the player to keep playing in a session that cannot be durably committed;
- provide a safe retry/restart path if recovery becomes possible;
- if a deliberate "start over" path is implemented, it must require explicit user intent and must preserve/quarantine the failed save material rather than silently destroying it.

Do not expose stack traces, raw local filesystem paths, native exception strings, or sensitive diagnostics to player copy.

Keep the UI scope proportional: this is a reliability campaign, not a visual redesign.

## Workstream F — regression tests first, then implementation

Add deterministic tests that fail on the current behavior before or alongside the fix.

### Repository-level mandatory tests

Extend `SaveLoadTests` (or a better-factored equivalent) to prove:

1. `Empty` remains distinct from fatal failure.
2. corrupt main + corrupt backup returns `Failed` and preserves both.
3. newer/incompatible schema returns `IncompatibleSchema` and preserves source bytes.
4. corrupt main + valid backup recovers.
5. the **first save after backup recovery** cannot destroy the last trusted backup under any injected failure point.
6. successful recovery save establishes a valid new authoritative main and leaves a trustworthy recovery copy.
7. interruption/fault scenarios do not leave a state where both main and backup are untrusted when one was good before the operation.
8. stale temp cleanup cannot delete the only trusted profile.

Use byte/content assertions where appropriate, not only `Exists()`.

### Application/runtime mandatory tests

Add tests at the lowest layer that can prove application semantics without Unity where practical, plus PlayMode coverage for actual `GameHost` lifecycle behavior when a licensed editor is available.

Mandatory scenarios:

- no-save bootstrap -> fresh profile -> persistence allowed;
- valid-save bootstrap -> normal profile;
- backup-recovery bootstrap -> recovered profile and safe subsequent persistence;
- corrupt main+backup bootstrap -> **no normal writable fresh profile**;
- incompatible-schema bootstrap -> **no normal writable fresh profile**;
- background/focus/pause/destroy after fatal boot does not alter main or backup bytes;
- gameplay mutation attempts in blocked state do not change durable/canonical progression;
- injected `Persist()` failure during a durable gameplay action does not leave the action committed in canonical state or success-only UI state;
- once persistence returns healthy through an allowed recovery flow, normal gameplay resumes without duplicate rewards or lost dedup cursors.

Do not make the standalone domain harness pretend to run Unity lifecycle tests. Keep evidence tiers honest.

## Workstream G — broad regression audit

Because this campaign changes a cross-cutting persistence boundary, inspect effects across the **entire codebase**, not just persistence files.

Specifically re-check:

- exactly-once movement reward invariants across save/restart;
- stale Expedition recovery;
- production checkpoints/offline accrual;
- Builder -> save -> reload -> Explore canonical placement;
- project prerequisites/cost spending;
- save validator/migrations;
- event subscriptions and presentation refresh;
- debug tools and release gating;
- privacy/logging hygiene;
- scene/bootstrap assumptions about `GameHost.Profile` always being non-null.

Fix any Critical/High regression introduced or exposed by the persistence-health change within this campaign. Do not use the campaign as permission for unrelated feature work.

## Constraints / non-goals

- Do **not** build Region 2, multiplayer, cloud save, combat, live ops, or other Phase 9 expansion.
- Do **not** add a backend merely to solve local save safety.
- Do **not** weaken exactly-once activity semantics.
- Do **not** require GPS for passive steps.
- Do **not** fabricate Unity/editor/device evidence.
- Do **not** bypass Unity licensing, UAC/elevation, platform signing, or hardware requirements.
- Do **not** delete or silently reset incompatible/corrupt saves.
- Do **not** change the save schema unless the final design truly requires persisted model changes. If it does, increment/migrate/test/document it according to repository rules.
- Do **not** assume a particular AI model, coding harness, operating system shell, or sub-agent system. Use whatever execution environment is actually available.

## Validation gates

Run every gate that is genuinely available after implementation and record exact results:

1. `dotnet test verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj`
2. `scripts/verify-domain.ps1` or platform-equivalent invocation if appropriate
3. `scripts/verify-release-hygiene.ps1`
4. `scripts/verify-unity-static.ps1`
5. `git diff --check`
6. Unity EditMode: `scripts/verify-unity-editmode.ps1` **only if a licensed pinned editor session is actually available**
7. Unity PlayMode: `scripts/verify-unity-playmode.ps1` **only if actually available**

If the current environment is still blocked by the known Unity account/license condition, state EDITOR gates as **UNVERIFIED** and provide the exact reproducible command. Do not treat that environmental block as permission to skip deterministic non-editor tests.

No physical-device gate is required for this campaign unless implementation unexpectedly touches native providers; avoid doing so unless necessary.

## Completion / acceptance gates

This campaign is complete only when all of the following are true:

1. Only a genuinely empty repository auto-creates a new profile.
2. Fatal/incompatible load states cannot autosave over existing save material.
3. Failed/incompatible save bytes remain preserved until explicit destructive recovery.
4. Backup recovery cannot turn the previously trusted backup into corrupt backup during an interrupted next save.
5. At least one trusted last-known-good copy survives every tested file-operation fault point.
6. Durable gameplay mutations have explicit persistence-failure semantics; no Critical path reports durable success while disk state remains old/failed.
7. Fatal persistence state blocks or safely contains all normal progression mutation paths.
8. Recovery/failure UI is truthful and does not encourage play that cannot be safely saved.
9. Existing exactly-once activity, Builder/Explore canonical state, offline production, restoration, and save-migration regressions remain passing at every available evidence tier.
10. New regression tests cover repository and application boot/lifecycle behavior described above.
11. `docs/IMPLEMENTATION_STATUS.md` is updated with the campaign's exact evidence, new test count, remaining environment blockers, and no overclaiming.
12. Any architecture/data-model contract changed by the implementation is updated in the relevant docs and an ADR is added if the decision is materially architectural.
13. No unresolved Critical/High defect attributable to this campaign remains unaddressed.
14. Working tree is clean after the final commit and push.

## Git and reporting requirements

- Work on the target branch unless repository state requires a safe reconciliation first.
- Do not overwrite unrelated concurrent work.
- Make coherent checkpoint commits if useful, but the final pushed history must leave `main` buildable/testable at available tiers.
- At campaign completion, update this file from `Status: ACTIVE` to `Status: COMPLETE` and append a concise executor report containing:
  - start SHA and final SHA;
  - files/systems changed;
  - root causes fixed;
  - tests added/changed;
  - exact validation commands/results;
  - any EDITOR/DEVICE gates still UNVERIFIED and why;
  - any lower-priority follow-up that was deliberately deferred.
- Use a **detailed final commit message that serves as a full session report**, not a vague one-line summary.
- Push all campaign commits to the configured upstream before stopping.
- If interrupted and this prompt remains ACTIVE, `/goal continue` must resume from the first genuinely incomplete requirement after reconciling current Git/implementation state; do not restart already-landed work.

## Executor behavior

Proceed autonomously through all unblocked work. Do not stop merely to ask what to do next when the repository, tests, and this prompt provide enough information. When implementation choices are ambiguous, select the smallest design that satisfies the documented invariants and prove it with tests. If an external gate is blocked, record it honestly and continue every unblocked workstream.

The campaign is about **making loss of player progress structurally difficult**, not about making failure messages prettier. Correct durability semantics are the acceptance criterion.

---

## Executor report (M8.1 campaign complete)

- **Start SHA:** `02261f5` (post-pull HEAD; prompt planned from `0cdf823`, no intervening M8.1 work landed). **Final SHA:** this commit.
- **Systems changed:**
  - `Persistence/SaveAbstractions.cs` + `FileSaveRepository.cs` — trust-checked rotation, byte-preserving quarantine (`<slot>.quarantined`), forward-schema save refusal, new `RecoveredFromBackupForwardSchema` result, `ISaveRepository.QuarantineAll`.
  - `Persistence/PersistenceCoordinator.cs` (new) — `PersistenceHealth`/`PersistencePolicy` boot+mutation policy, transactional `Commit` with revert/fatal outcomes.
  - `Persistence/ProfileStateCopier.cs` (new) — hand-written IL2CPP-safe in-place profile rollback; `Core/IPostCopyRepair.cs` repairs derived dedup indexes.
  - `App/GameHost.cs` — health state machine, fail-closed boot, `CommitChanges()` containment, lifecycle-autosave gating, blocked-mode scene composition, `RetryLoadFromDisk`/`StartOverWithFreshProfile`.
  - `App/SaveRecoveryController.cs` (new) — recovery UX per Workstream E.
  - `App/{ActivityTicker,ExpeditionController,AppFlowController,UiComposer}.cs` — all durable mutations routed through `CommitChanges`; success-only feedback suppressed on failed commits; truthful save-health copy.
- **Root causes fixed:** fresh-profile fabrication over Failed/Incompatible loads; corrupt-main-over-trusted-backup rotation hazard; forward-schema rewrite path; fire-and-forget persistence lying about durability; lifecycle autosave overwriting preserved bytes.
- **Tests added:** `SaveIntegrityApplicationTests` (10), `SaveLoadTests` (+9 incl. six-point interruption matrix), PlayMode blocked-boot/start-over gates in `RuntimeCertificationTests` (2, editor-gated).
- **Validation (exact commands/results):** `dotnet test verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj` → **144/144 PASS (was 124 pre-campaign)**; `scripts/verify-domain.ps1` → PASS; `scripts/verify-release-hygiene.ps1` → PASS (61 runtime sources); `scripts/verify-unity-static.ps1` → PASS (99 assets / 99 metas); `git diff --check` → clean.
- **UNVERIFIED gates (unchanged environment blockers):** Unity EditMode/PlayMode (`scripts/verify-unity-editmode.ps1` / `-playmode`) require a licensed pinned editor session; Android/iOS device tiers require modules/hardware per IMPLEMENTATION_STATUS. PlayMode coverage for blocked boot is committed but not executed here.
- **Deliberately deferred:** none within scope; no schema change was required (quarantine files are additive artifacts; `entries` remains canonical for dedup stores).