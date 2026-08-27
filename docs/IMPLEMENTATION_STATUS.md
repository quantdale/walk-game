# Implementation Status

Tracks what exists in code versus the ROADMAP phases, tiered by the evidence actually
produced. Acceptance criteria that require physical devices or an installed editor are
deliberately left unclaimed.

Evidence tiers:

- **AUTOMATED** — verified by the standalone domain suite (`scripts/verify-domain.ps1`).
- **EDITOR** — requires the pinned Unity editor with a valid license and is only claimed
  when the editor compile/test logs prove it.
- **DEVICE** — requires physical/emulated mobile hardware.
- **UNVERIFIED** — claimed by no evidence yet.

Last updated: 2026-08-28 (M8.8 re-audited completion campaign; current top-level evidence below)

## Current verification status — M8.8 re-audit

- Source was reconciled onto `main` at `15947a222b9812cb641066f40cb8e48a276207c7`; the
  campaign writer lease is held on this worktree. Repository identity passed before source
  and documentation changes.
- Fresh headless evidence after the M8.8 changes: domain suite **263/263 PASS**;
  `verify-domain.ps1` **PASS**; Unity static audit **112/112 PASS**; release-hygiene audit
  **63 runtime sources PASS**; `Test-AgentGuards.ps1` **43/43 PASS**; certification-script
  suite **71/71 PASS**; `git diff --check` **PASS**.
- H1 editor namespace, H2 strict migration, H5 API 36 source target, M1 spend validation,
  M2 saturating arithmetic, M3 shader guards, Android permission state-table coverage, and
  iOS callback/provider-lifetime source safeguards are implemented and headlessly covered.
  Source/static evidence is not Unity semantic-compile or device evidence.
- Unity semantic compile/import, EditMode, PlayMode, and first-import canonical project-state
  materialization are **UNVERIFIED**: this host has no Unity editor executable or licensed
  session. `scripts/verify-unity-compile.ps1` fails closed when `UNITY_EDITOR_PATH` is absent.
- Android SDK inventory is present for platform `android-36` and build-tools `36.0.0`, but
  Unity Android Build Support/IL2CPP is unavailable, no APK was generated, and `adb devices`
  reports no connected target. Android build, lifecycle, API 36 generated-manifest evidence,
  and physical step-counter exactly-once evidence are **UNVERIFIED**.
- iOS Xcode generation/build/device evidence is **UNVERIFIED**: this host is Windows with no
  macOS, Xcode, iOS SDK, signing environment, or iOS device. The repeatable macOS wrapper and
  deterministic bundle/project checks are committed.

Older phase and campaign sections below are historical evidence unless explicitly labeled
with the current M8.8 date and counts.

## Phase 0 - Foundation

| Item | State |
| --- | --- |
| Pin Unity 6000.3.4f1 | ProjectVersion.txt pins it; exact editor executable reported `6000.3.4f1` — **AUTOMATED/environment**; licensed import **UNVERIFIED** |
| URP project | Package pinned + setup menu assigns pipeline asset on first open — **UNVERIFIED** (editor step) |
| Android/iOS build profiles | Editor menu (`WalkGame/Setup/Apply Product Identity`) and batch entry point — **UNVERIFIED** (editor installation/license gate) |
| .gitignore | Done; verification harness projects are commit-included |
| Assembly definitions | All assemblies present. Campaign fix (ADR 0004): platform assemblies compile on every target with source-level `#if` guards; `noEngineReferences` corrected for the Android JNI interop assembly. Missing deterministic `.meta` files added — **AUTOMATED** (static audit; Unity compile unverified) |
| Input System | Package pinned; active-input toggle in setup menu — **UNVERIFIED** (editor step) |
| Test assemblies | WalkGame.Tests.EditMode + standalone harness share sources — **AUTOMATED** |
| Bootstrap scene | Scene YAML valid; GameHost GUID reference matches its committed meta — **AUTOMATED** (static); Play Mode **UNVERIFIED** |
| Service composition root | `App/GameHost.cs`; provider selection never falls back to debug for permission reasons — **AUTOMATED** |
| ClockService | IClock + system/mutable/offset variants; all economic/lifecycle time flows through it (ADR 0005-era campaign fix) — **AUTOMATED** |
| Save serializer/repository | Atomic writes + backup recovery + sequential migration — **AUTOMATED** |
| Logging wrapper | Done — **AUTOMATED** |
| Debug/dev menu shell | Done, dev-build gated — **AUTOMATED** (domain-level) |

## Phase 1 - Gray-box region and canonical state

RegionDefinition/RegionState/stable IDs per DATA_MODEL.md; Ashfall Basin
content-as-code with integrity tests; buildings instantiate from state via
`RegionPresenter`/`BuildingActor`; single-scene MVP load; save schema v1 with
sequential migrator; deterministic fixtures — **AUTOMATED**.
The runtime now contains a reusable procedural Ashfall environment kit (routes, settlement,
river/waterworks, grove/wetland, workshop, greenhouse, research, residential district,
transit gate, story spaces, and stage-driven ambience). Code/static readiness is
**AUTOMATED**; editor visual inspection remains **UNVERIFIED**.

## Phase 2 - Restoration and builder loop

VitalityLedger, debug provider (+1,000 steps), step-to-Vitality conversion, project
definitions/prerequisites/transactions, ruin-to-restored swap, builder camera,
placement grid validation, move preview/confirm/cancel, persisted placement incl.
save/reload identity — **AUTOMATED**. Responsive touch/UI paths are implemented; on-device
touch behavior — **UNVERIFIED**.

## Phase 3 - Explore mode

Third-person controller + follow camera, explicit mode state machine, authored spawn,
canonical state shared by both views, boundary clamping, NPC/lore as domain state —
**AUTOMATED**. Scene-side NPC actors, lore markers, interaction prompts, authored anchors,
and stage ambience are implemented; PlayMode visual behavior — **UNVERIFIED**.

## Phase 4 - Native activity integration

Campaign-hardened (see ADR 0005):

- Permission lifecycle: contextual request API on every provider, coordinator state
  machine, denial-as-normal-state, Android lazy monitoring + Denied/NotDetermined
  refinement, iOS real first-request path — **AUTOMATED** (fake providers).
- Android cumulative counter: engine-free reconciler covering unknown-start baseline,
  reboot reset, NaN/infinity/negative fail-closed, repeated values, implausible-jump
  cap, persisted cursor across process restarts — **AUTOMATED**
  (`AndroidCounterReconciliationTests`).
- Non-blocking pipeline: no `.Result`/`.Wait()` on gameplay paths; async iOS bridge
  with stale-result protection; failed queries leave cursors untouched — **AUTOMATED**
  at planning/domain level; native callback marshalling itself is **UNVERIFIED**
  (requires device/Xcode).
- Device checklist (permission dialogs, reboot/resume on hardware, logcat evidence):
  **UNVERIFIED**. An API-36 emulator (`sdk_gphone64_x86_64`, `emulator-5554`) is
  connected, but `dumpsys sensorservice` exposes no `TYPE_STEP_COUNTER`; it cannot
  certify real movement. The committed Android bridge now reports unavailable when the
  sensor is absent, and the fake-provider lifecycle cases remain **AUTOMATED**.

## Phase 5 - Idle/incremental systems

ProductionService checkpoint math, offline cap, backward-clock clamp, storage caps,
tier multipliers, collection API, offline resume summary, producer status rows, and
dedicated HUD collect buttons refreshed on ticker cadence — **AUTOMATED**.

## Phase 6 - Vertical-slice content

Ashfall Basin: 15 restoration projects, 9 building instances, 4 producers, 3 NPCs,
5 lore objects, milestone ladder, stages 0-3, scripted full-playthrough test reaching
the transit-gate finale — **AUTOMATED**. Canonical-state-derived visual stages, restoration
feedback hooks, responsive project/producer HUD, onboarding, and Expedition presentation
are code/static-ready; Unity runtime presentation — **UNVERIFIED**.

## Exactly-once movement rewards

Central invariant enforced across passive polling, Expeditions, process restarts and
save reloads (campaign S8): session-id dedup store, passive suppression during active
sessions with cursor partitioning, durable interval keys (a serializer defect that had
been silently dropping them was found and fixed). Regression suites:
`ActivityServiceTests`, `AndroidCounterReconciliationTests`, `SaveLoadTests` —
**AUTOMATED**. Real-device double-count probing — **UNVERIFIED**.

## Phase 7A — non-hardware hardening

- Persistence fault injection: **AUTOMATED**. Tests cover interruption before temp
  completion, failure after temp creation, backup-copy failure, main deletion before
  replacement, missing/corrupt main with valid backup, both files corrupt, unsupported
  schema, migration failure, and injected write/copy failures. The repository reports
  failure and preserves the last recoverable save; it never silently wipes it.
- Clock/time anomalies: **AUTOMATED**. Tests cover backward time, future timestamps,
  offline caps, UTC persistence, and fake-clock production. Future restoration timestamps
  produce an explicit validation anomaly report and warning, while production rejects
  future elapsed time rather than granting progression.
- Privacy/logging: **AUTOMATED/static audit**. No raw GPS coordinates, continuous routes,
  or native sensor payloads are logged or persisted. The Android manifest declares only
  optional step-counter hardware plus `ACTIVITY_RECOGNITION`; no location permission is
  mandatory. Save diagnostics no longer include local save paths. Release logging is
  warning-level; device log review remains **UNVERIFIED**.
- Performance readiness: **AUTOMATED/static review**. The presentation uses shared
  materials/property blocks for repeated geometry, reused UI rows, a non-allocating
  Explore interaction scan, checkpoint production math, and reduced-motion gating for
  particles. No FPS/allocation/thermal numbers are claimed; physical-device profiling
  remains **UNVERIFIED**.

## Phases 7B-9

Physical Android/iOS lifecycle, sensor, performance, store, playtest, and post-MVP
expansion gates remain unverified or intentionally out of scope for this campaign.

## M8.1 save-integrity record (ADR 0007)

Fail-closed persistence campaign executed from `.agent/EXECUTION_PROMPT.md`:

1. **Boot no longer fabricates profiles over failed saves** — only an empty save
   directory auto-creates one (`PersistencePolicy.HealthForBoot`); `Failed`,
   `IncompatibleSchema`, and the new `RecoveredFromBackupForwardSchema` boot into a
   blocked recovery mode where no gameplay service, ticker, rig, or HUD exists and
   lifecycle autosave cannot write — AUTOMATED (policy mapping; PlayMode blocked-boot
   lifecycle gates committed but UNVERIFIED until a licensed editor run).
2. **Trusted-backup rotation invariant proven** — the pre-campaign algorithm copied a
   corrupt main over the trusted backup during the first post-recovery save;
   `FileSaveRepository.Save` now read-backs the main slot first, quarantines corrupt
   material byte-for-byte to `<slot>.quarantined`, seeds the backup from validated
   payload before touching main, and refuses rotation over forward-schema evidence —
   AUTOMATED (`SaveLoadTests` interruption matrix across six fault points plus
   success/evidence cases).
3. **Transactional commits** — every player-visible durable mutation goes through
   `GameHost.CommitChanges()`/`PersistenceCoordinator`; a failed write reverts the
   canonical graph IN PLACE to exact disk truth via the hand-written IL2CPP-safe
   `ProfileStateCopier` (reference-preserving for services/providers/actors), or the
   host enters blocked state on fatal loss; collection/restoration/expedition/
   onboarding/settings paths suppress success feedback on failure and surface truthful
   copy — AUTOMATED (coordinator outcomes incl. exactly-once replay consistency after
   rollback; serialized-graph fidelity gate).
4. **Truthful recovery UX** — `SaveRecoveryController` replaces the playable runtime in
   blocked mode: plain-language explanation per failure class, in-place load retry,
   two-tap-confirmed "start over" that quarantines instead of deletes; the misleading
   "session is still playable" copy is gone — static hygiene PASS; visual behavior
   UNVERIFIED (editor).
5. **Docs** — ADR 0007 added; TECHNICAL_ARCHITECTURE §15 and DATA_MODEL §20 extended
   with the health/transaction contract.

Environment blockers unchanged from M8: no licensed Unity editor session, no Android
Build Support module, no macOS/Xcode, no genuine step-counter hardware. All EDITOR- and
DEVICE-tier gates remain UNVERIFIED; every unblocked deterministic gate was run this
campaign (144/144 domain, hygiene audit, static audit, `git diff --check`).

## M8 runtime-defect record

Defects discovered by first-import/runtime inspection and fixed this campaign:

1. **Stale Expedition suppression marker** — a process kill mid-Expedition persisted
   `activeSession`, which suppressed every future passive snapshot forever (no recovery path
   existed). Fixed with boot-time `ActivityService.RecoverInterruptedSession()`; recovery
   credits nothing itself and movement made during the interruption re-reads from the provider
   cursor exactly once. Covered by `InterruptedSessionRecoveryTests` + PlayMode boot gate.
2. **Missing uGUI EventSystem** — all UI was composed programmatically but nothing ever created
   an EventSystem, so every button and the Explore joystick rendered yet were inert. Fixed via
   `UiRuntime.EnsureEventSystem` during UI composition; PlayMode regression gate added.
3. **Literal `\n` in project panel text** — restoration rows/details displayed raw backslash-n
   instead of line breaks. Fixed strings in `ProjectPanelController`.
4. **Event-subscription leaks** — `UiComposer`/`AppFlowController` subscribed to domain events
   without ever unsubscribing; stale handlers would fire into destroyed UI across scene reloads.
   Both now detach every subscription on destroy.
5. **Silent feedback layer** — audio cues were wired but no clips existed anywhere, so settings
   showed values with no audible effect. Procedural stand-in cues + honest ambient loop now
   honor master/music/effects/haptics until final production audio lands.
6. **Dead-end finale copy** — completing all 15 projects still pointed players at "the next
   locked project". Full completion now communicates the milestone cleanly (§19).

## Environment record for this campaign

- Start SHA `bc3fd7f9831ebacda936090266abe5d73ebd0d45` (M8 campaign), clean tree, synced
  with `origin/main`. Checkpoints `3a0244a` (runtime bring-up fixes) and `98776ac`
  (Android/smoke/hygiene gates) are committed to `main`.
- Available tooling: .NET SDK 8.0.424; Unity Hub 3.21.0 (MSIX, running); exact Unity editor
  `6000.3.4f1`; JDK 17.0.20; Android SDK platforms 36/37.0, build-tools 35/36,
  NDK 27.0/27.1; `adb` 37.0.1; API-36 emulator `emulator-5554` (no TYPE_STEP_COUNTER).
- Licensing/installation (M8 re-investigation): the licensing client is healthy and online
  but no Unity account session exists on the machine (`accounts.db`: zero accounts; client
  log: "Token not found in cache"; all entitlements `granted: False`). The prior module
  install died at UAC elevation (`ELEVATION_CANCELLED`) and this session is not elevated.
  Activation requires a user account sign-in outside this environment.
- Not available: macOS/Xcode, physical iOS/Android hardware, an Android emulator exposing a
  genuine step-counter sensor, and a licensed Unity editor session.
- Honest consequence: all EDITOR- and DEVICE-tier gates above remain **UNVERIFIED** even
  where the underlying code was fixed and domain-tested. Every blocked gate is reproducible
  via the scripts named in this document once its specific precondition (license, module,
  or hardware) is met.

## M8 certification matrix

| Gate | Result | Evidence |
| --- | --- | --- |
| Domain suite | PASS | 144/144 (`dotnet test`, this campaign) |
| Unity static audit | PASS | 99 assets/99 metas, pin + manifest invariants (`verify-unity-static.ps1`) |
| Release hygiene / privacy audit | PASS | 61 runtime sources, minimal manifest (`verify-release-hygiene.ps1`) |
| `git diff --check` | PASS | clean output at final gate run |
| Unity project import | UNVERIFIED | requires licensed editor |
| Unity compilation | UNVERIFIED | requires licensed editor |
| EditMode | UNVERIFIED | committed sources compile under the standalone harness; editor run blocked by license |
| PlayMode | UNVERIFIED | suite extended (EventSystem + boot-recovery gates); editor run blocked by license |
| Ashfall complete playthrough | PASS | `AshfallTests.DeadWorld_To_TransitGateAlignment_CompletesInDependencyOrder` |
| Economy pacing replay | PASS | `AshfallEconomyPacingTests` (casual-walker window; idle-only completes nothing) |
| Exactly-once activity | PASS | `ActivityServiceTests` + `InterruptedSessionRecoveryTests` (incl. save/reload) |
| Save fault injection | PASS | `SaveLoadTests` (M8.1 trusted-rotation matrix); producer-prune regression lives in `PlayerExperienceTests.EnsureProducerStates_PrunesUnknownPersistedProducerIds` |
| Save-health boot policy & rollback containment (M8.1) | PASS | `SaveIntegrityApplicationTests` (policy mapping, coordinator outcomes, copier graph fidelity) |
| Blocked-boot lifecycle & start-over quarantine (M8.1) | UNVERIFIED | PlayMode gates committed in `RuntimeCertificationTests`; requires licensed editor |
| Android build | UNVERIFIED | AndroidPlayer module absent; build script release-shaped and ready |
| Android install/launch/lifecycle smoke | UNVERIFIED | `verify-android-smoke.ps1` committed for first emulator/device |
| Real step sensor | UNVERIFIED | physical device required |
| iOS compile/device | UNVERIFIED | no macOS/Xcode |
| Performance measurement | UNVERIFIED | structural measures only; profiling needs hardware |

## M8.2 campaign — repository isolation, concurrency guards & whole-repo audit

**Scope:** repair + repository isolation. Identity proof (`quantdale/walk-game`,
never the sibling `quantdale/simple-walk-game`), single-writer enforcement,
lost-update protection, hook/CI enforcement, a four-track whole-repository
breakage audit (persistence/M8.1 call-site trace; Unity asset/GUID integrity;
docs-claims-vs-tests; exactly-once/lifecycle sweep), and repairs for every
Critical/High plus state-integrity Medium finding.

### New enforcement infrastructure (AUTOMATED)

| Guard | Evidence |
| --- | --- |
| Repository identity guard | `.repo-identity.json` + `scripts/Assert-RepoIdentity.ps1` + `scripts/assert-repo-identity.sh`; validates root, identity file, normalized HTTPS/SSH origin, fingerprints, `GITHUB_REPOSITORY` under CI |
| Single-writer lease | `scripts/WriterLock.ps1` / `writer-lock.sh`; atomic untracked lock under `.git/`; stale recovery only via explicit `--force` with recorded provenance |
| Lost-update protection | `scripts/Check-RemoteAdvance.ps1` / `check-remote-advance.sh`; fetch + ancestry proof before integration |
| Tracked hooks | `.githooks/pre-commit`, `.githooks/pre-push` (identity guard; refuses remote deletion and force-shaped pushes); activation via `git config core.hooksPath .githooks` (`scripts/setup-hooks.*`) |
| CI gates | `repository-identity` job (hard `GITHUB_REPOSITORY` equality + `-CiMode` guard) and `agent-guards` job in `.github/workflows/domain-tests.yml` |
| Deterministic guard suites | `pwsh scripts/Test-AgentGuards.ps1` → **36/36 PASS** (twelve scenarios × pwsh/sh implementations + hook stdin tests), entirely against local fixture repos with `GIT_ALLOW_PROTOCOL=file` egress restriction |
| Policy docs | AGENTS.md identity contract first section + isolation/writer-lock/race/destructive-ops/recovery sections; ADR 0008; all harness goal adapters route through the identity guard |

### Whole-repo audit results and repairs (AUTOMATED unless noted)

| Finding | Severity | Disposition |
| --- | --- | --- |
| Lore discovery presented success text + fired celebration cue even when its commit rolled back | **High** | Fixed: `AppFlowController.Interact` now checks the commit outcome and shows truthful "could not be saved" copy; `GameHost.DurableCommitResolved` + deferred-cue queue in `FeedbackController` flush success cues only on durable commits |
| Restoration/milestone/expedition/placement cues fired before durability was known | Medium | Fixed by the same deferred-cue mechanism; placement confirm cue now outcome-gated; expedition finish cue queued |
| Onboarding getter mutated canonical `settings.onboardingStep` outside any persistence boundary on every HUD refresh | Medium | Fixed: derivation made pure; persisted advancement only via explicit advance/dismiss through `CommitChanges`, never regressing behind world facts |
| Debug region reset mutated durable state without persistence containment | Low (debug-only) | Fixed: routed through `CommitChanges` |
| `AchievementState.reachedMilestoneIds` could deserialize null → NRE on next milestone award and during rollback copies | Medium | Fixed in `SaveValidator` + regression test |
| Negative `lifetimeVerifiedDistanceMeters` passed validation into bonus math | Medium | Fixed: clamped with warning + regression test |
| Malformed/null producer entries and negative/future producer checkpoints passed validation | Medium | Fixed: prune + clamp mirroring building rules + regression test |
| Dead destructive `FileSaveRepository.DeleteAll` API (zero callers) invited bypassing quarantine semantics | Low | Removed from interface+implementation; ADR 0007 amended |
| `TECHNICAL_ARCHITECTURE.md §14` mandated save fields (`appVersion`, timestamps) absent from schema v1 | High (doc/code contradiction) | Contract doc corrected to match DATA_MODEL and implementation |
| Stale audit counts in this document (94/94 metas, 57 sources) presented as current evidence | Medium | Refreshed (99/99, 61); test-attribution pointer fixed |
| URP/input settings exist only after first licensed setup run; unused Unity modules pinned (incl. `unityanalytics`) | Medium | Deferred with rationale: unverifiable without an editor session; recorded as follow-up below |
| Failed-commit rollback permanently drops (never doubles) the already-drained Android sensor window | Low | Deferred: direction-safe under exactly-once priority; documented follow-up |

Unity asset graph verified clean beyond the static script: 198 Assets files,
bidirectional meta pairing, zero orphaned metas, 99 GUIDs collision-free, both
scene script references resolve with matching MonoBehaviour classes,
EditorBuildSettings GUIDs consistent. Exactly-once activity pipeline, Builder/
Explore canonical-state projection, injected-clock discipline, startup failure
paths, dedup rollback repair, and the standalone-harness source selection were
audited clean.

### M8.2 certification matrix

| Gate | Result | Evidence |
| --- | --- | --- |
| Domain suite | PASS | 146/146 (`dotnet test` / `verify-domain.ps1`, this campaign) |
| Unity static audit | PASS | 99 assets/99 metas (`verify-unity-static.ps1`) |
| Release hygiene / privacy audit | PASS | 61 runtime sources (`verify-release-hygiene.ps1`) |
| Agent guard suites | PASS | 36/36 scenarios (`Test-AgentGuards.ps1`, pwsh + sh matrices) |
| `git diff --check` | PASS | clean at final gate run |
| Cross-repo contamination grep | PASS | sibling references only inside protection mechanism |
| Repository identity (live) | PASS | `assert-repo-identity.sh` exit 0 against real checkout |
| Unity EditMode/PlayMode | UNVERIFIED | licensed editor session still blocked by account-level licensing |
| Android/iOS device tiers | UNVERIFIED | unchanged environment blockers |

### Deferred follow-ups (deliberate, documented)

1. Commit generated URP/Graphics/Input settings after first licensed editor run;
   then add a static check asserting them.
2. Trim unused pinned Unity modules (privacy posture + IL2CPP size) once an
   editor build can validate the manifest change.
3. ~~Consider re-paying drained-but-uncommitted Android step windows after failed
   commits without violating exactly-once~~ — **RESOLVED by M8.3** (ADR 0009:
   prepared-delivery claims restore rejected movement for exactly-one retry).
4. ~~`ActivityTicker` issues a no-op commit when passive processing produced no
   mutation (harmless write every cadence)~~ — **RESOLVED by M8.3** (explicit
   `PassiveReconciliationResult`; commits only on durable mutation).

## M8.3 campaign — movement delivery durability & activity write discipline

**Scope:** the provider-side half of the activity transaction left deferred by
M8.2 (follow-ups 3 and 4 above). Android and debug providers consumed/drained
movement before `CommitChanges()` proved durability; a failed save rolled the
profile back while provider-private state stayed advanced, permanently losing
the window (drop-never-double). Expedition completion had the same defect
class, and the ticker committed on every cadence pass regardless of whether
anything canonical changed.

Start SHA `8d1f9c7250c0ac72b59dc3b8958ffeef94bf6a5d` (planner handoff at
`main@15384b6b` + planner-only advance reconciled), branch
`agent/walk-game/m8.3-fc6b02fe`, single-writer lease held for the session.

### Contract (ADR 0009) — AUTOMATED

| Change | Evidence |
| --- | --- |
| `IActivityProvider` two-phase delivery | `PreparePassiveDeliveryAsync` / `ResolvePreparedDelivery` / `ResolveSessionCompletion` replace `ReadSnapshotAsync`; all implementations and callers migrated together |
| Explicit mutation outcome | `ActivityService.ProcessPassiveSnapshot` returns `PassiveReconciliationResult` (`NoDelivery`, `SuppressedBySession`, `DuplicateDurable`, `DurableMutation`) instead of ambiguous accepted-steps count |
| Android claim state machine (engine-free) | `AndroidCounterReconciler.ClaimPending/AcknowledgeClaim/RestoreClaim`: single open claim, idempotent resolution, restored pending survives reboot/anomaly rebaseline; runtime baseline intentionally ahead of rolled-back cursor is proven safe both in-process and across restart |
| Debug provider mirrors production | staging instead of zeroing; reject restores the fake counter; **latent double-credit fixed**: simulated session steps previously stayed in the passive counter after a durably credited session |
| iOS conformance | preparation consumes nothing private; resolutions are no-ops; failed commit rewinds durable cursor so the identical history window retries |
| Expedition completion durability | providers hold base steps between stop and resolution; rejected saves return them to the passive stream; same-process result replay retries until durably marked; UI copy stays truthful (M8.2 mechanism untouched) |
| Ticker write discipline | 30s cadence commits only on `DurableMutation`; suppressed deliveries are rejected back to the provider; proven duplicates acknowledge without any profile write; legacy unconditional `providerCursor = null` write removed |
| Regression coverage | new `MovementDeliveryDurabilityTests` (14 scenarios incl. fault-injected real rollback/retry), extended `AndroidCounterReconciliationTests` (+6 claim scenarios), disposition assertions in `ActivityServiceTests`/`InterruptedSessionRecoveryTests`/`PlayerExperienceTests` |

### M8.3 certification matrix

| Gate | Result | Evidence |
| --- | --- | --- |
| Domain suite | PASS | **165/165** (`dotnet test` + `verify-domain.ps1`, this campaign; baseline was 146/146) |
| Unity static audit | PASS | 100 assets/100 metas (`verify-unity-static.ps1`, after adding the new fixture meta) |
| Release hygiene / privacy audit | PASS | 61 runtime sources (`verify-release-hygiene.ps1`) |
| Agent guard suites | PASS | 36/36 (`Test-AgentGuards.ps1`; requires Git Bash ahead of WSL `bash.exe` on PATH — first run's 12 sh-matrix failures were environmental path shadowing, not product regressions) |
| Repository identity (live) | PASS | `Assert-RepoIdentity.ps1` exit 0 pre-work and post-work |
| `git diff --check` | PASS | clean at final gate run |
| Unity EditMode/PlayMode | UNVERIFIED | licensed editor still blocked by account-level licensing (repro: sign in, activate, `scripts/verify-unity-editmode.ps1`) |
| Android/iOS device tiers | UNVERIFIED | unchanged environment blockers; Android/iOS provider code paths compile under platform guards only |

### Deliberate remaining limitations

- Provider claim/restoration state is in-memory by design: crash recovery
  derives from the persisted raw-counter/sync cursors plus native absolute or
  historical facts, so no serializer or migration change was needed.
- A fatal (non-revertible) persistence loss still tears down all services per
  ADR 0007; movement observed inside such a window is unrecoverable and that
  tier remains fail-closed rather than synthesized.

## M8.4 campaign — runtime orchestration durability & headless certification (ADR 0010)

**Scope:** the application orchestration half left deliberately outside the M8.3 headless gate:
`ActivityTicker`, `ExpeditionController`, `GameHost` lifecycle/persistence glue, and the
transaction ordering that determines what happens after provider completion, domain mutation,
commit success/failure, rollback, and provider resolution. PlayMode coverage exists but stays
UNVERIFIED without a licensed editor; the commit-to-resolution ordering had no headless proof.

Start SHA `e128a46f2929d8e54e639a66aeba1b77d2553347` (M8.4 planner handoff reconciled from
`main@c7d18f766438eb50fbb3854d88a9972fdbc5dc32`), branch
`agent/walk-game/m8.4-exec-20260826`, single-writer lease `sess-m84-exec-20260826`.

### Root cause — planner defect reproduced as predicted

If an Expedition `activeSession` marker was previously persisted by lifecycle autosave
(`GameHost.Persist()` on pause/focus during an active session), `ExpeditionController`
cleared it in memory before `ProcessSessionResult` → `CommitChanges()`. A failed commit
reverted the profile via `ProfileStateCopier` and restored the durable marker, then
`ResolveSessionCompletion(sessionId, false)` returned base steps to the passive stream where
`ActivityService.ProcessPassiveSnapshot()` suppressed them as `SuppressedBySession`. The same
defect applied to fault/cancel/null stop paths (uncommitted abandonment) and to timeout/late
passive preparation (stranded provider claim). Fatal commits NRE'd on the next provider line.

### Fix (ADR 0010) — AUTOMATED

| Change | Evidence |
| --- | --- |
| Engine-free `ActivityTransactionCoordinator` (`WalkGame.Activity`) | stateless policy `CompleteExpedition` / `DeliverPreparedPassive` / `RejectAbandonedPreparation`; owns trust evaluation, process→commit→resolve→repair ordering and fatal-loss divergence |
| `GameHost.CommitChangesWithOutcome()` | three-way outcome (`Committed` / `RevertedToLastKnownGood` / `FatalPersistenceLoss`) with identical `PersistenceReverted` / `DurableCommitResolved` event semantics; existing `CommitChanges()` wraps it |
| `WalkGame.Activity` → `WalkGame.Persistence` assembly reference | coordinator can see commit outcome without duplicating the enum |
| `ExpeditionController.RunExpedition` rewrite | captures provider/activity before fatal teardown; delegates both result and no-result paths to coordinator; repairs resurrected marker in same process; truthful `StatusMessage` gating on `Committed` vs `Reverted` vs `Fatal` |
| `ActivityTicker.ReconcileRoutine` rewrite | captures refs before fatal; timeout path drains late completion on the main thread and rejects unprocessed delivery (`durable=false`, cursor untouched) — superseded in M8.5 by the terminal-ownership lease (ADR 0011), which removes the 30 s hard cap entirely; commit path delegates to coordinator; NRE on fatal fixed |
| `ActivityTicker.CompleteSessionRoutine` (debug) | same coordinator path so the debug menu matches the real Expedition transaction |
| Passive revert repair | `DeliverPreparedPassive` now also repairs a resurrected marker after `Reverted`, so an expedition-fail→passive-fail chain does not strand the next retry |
| Regression coverage | `ApplicationOrchestrationTests` (17 scenarios: F1–F14 mandatory plus variants), extensions to `ActivityServiceTests`, `AndroidCounterReconciliationTests`, `SaveLoadTests`; all 185 headless tests pass |

### M8.4 certification matrix

| Gate | Result | Evidence |
| --- | --- | --- |
| Domain suite | PASS | **185/185** (`dotnet test` + `verify-domain.ps1`, this campaign; baseline was 165/165) |
| Unity static audit | PASS | 102 assets/102 metas (`verify-unity-static.ps1`) |
| Release hygiene / privacy audit | PASS | 61 runtime sources (`verify-release-hygiene.ps1`) — coordinator contains no sensor/GPS/save-path logging |
| Agent guard suites | PASS (ps) | 24/24 ps tier; 12 sh-tier failures are the known WSL `bash.exe` path shadowing (see M8.3 matrix) |
| Repository identity (live) | PASS | `Assert-RepoIdentity.ps1` / `assert-repo-identity.sh` exit 0 pre- and post-work |
| `git diff --check` | PASS | clean at final gate run |
| Unity EditMode/PlayMode | UNVERIFIED | licensed editor still blocked by account-level licensing (repro: sign in, activate, `scripts/verify-unity-editmode.ps1`); new PlayMode timing paths remain UNVERIFIED by construction |
| Android/iOS device tiers | UNVERIFIED | unchanged environment blockers; provider code paths compile under platform guards only |

### Deliberate remaining limitations

- Provider claim/restoration state stays in-memory by design (see ADR 0009); crash recovery derives from persisted cursors plus native absolute/history facts. ~~A provider task that never completes after the 30 s hard cap may leave a claim open until restart~~ Superseded by M8.5/ADR 0011: abandoned operations keep a deterministic cleanup owner forever, so no claim can be stranded by timing. A provider task that NEVER completes is a provider bug and still holds its own staged window (single-open-claim rule keeps later windows safe).
- Fatal persistence loss remains fail-closed per ADR 0007; movement observed inside a fatal window is unrecoverable and no bonus is synthesized.
- Scene composition (`AppFlowController`, `Builder`/`Explore` rig, `UiComposer`) and provider JNI / CoreMotion callbacks remain UNVERIFIED without an editor/device.

## M8.5 campaign — runtime ownership & rollback fidelity (ADR 0011)

**Scope:** operation/instance ownership after M8.4: provider lifetime, cancellation vs
terminal-ownership semantics for async provider operations, Android claim identity,
convergence of every active-session completion path onto one transaction protocol,
durability-gated presentation truth, exact rollback graph fidelity, and dedup
canonicalization.

Start SHA `fb619e93df3db2c5b86a190a6dcea01efb64442f` (M8.5 planner handoff reconciled from
`main@616924fcbe61bc50a1c7f064b0fe6fe00fb185ba`), branch
`agent/walk-game/m8.5-exec-20260826`, single-writer lease `sess-m85-exec-20260826`.
Baseline re-run fresh this campaign: **185/185** headless PASS before changes.

### Defects fixed — AUTOMATED evidence

| Finding | Fix | Evidence |
| --- | --- | --- |
| H1 Android stale delivery resolution mutated newer claims | engine-free claim identity: `AndroidCounterReconciler.OpenClaimId` + `AcknowledgeClaim(id)` / `RestoreClaim(id)` resolve only the named claim; adapter binds `deliveryId` to it; stale/repeated/null/unknown are no-ops | `AndroidCounterReconciliationTests` identity suite incl. `StaleOrUnknownOrNull_Resolution_CannotMutateANewerClaim` |
| H2 ownerless late passive task (30 s drain admitted stranding) | engine-free `OperationLease` + `ProviderOperations.AbandonPreparation`: atomic exactly-one-terminal-owner transfer; cleanup continuation rejects any late delivery whenever it arrives; 12 s deadline stays scheduling-only | `OperationOwnershipTests.AbandonWins_CompletingLate_RejectsDeliveryExactlyOnce_ClaimNotStranded`, `.TimedOutThenLateDelivery_NextReconcileDeliversTheSameMovementOnce` |
| H3 no provider teardown contract | `IActivityProvider.Shutdown()` idempotent contract on Debug/Unavailable/Android/iOS: stops native monitoring/live updates, refuses new work, RESTORES claim/completion state instead of consuming, never fabricates durable ack; `GameHost.ShutdownProvider()` runs BEFORE graph drop on blocked transition, retry-load, start-over, destroy | `ProviderLifetimeTests` (5 scenarios); ordering enforced in `GameHost.EnterBlockedState` / `RetryLoadFromDisk` / `StartOverWithFreshProfile` / `OnDestroy` |
| H4 unbounded Expedition tasks | bounded policy waits (start/poll 10 s, stop 30 s) with lease-owned late-result disposal; hung stop routes through shared no-result close plus non-durable late resolution | `RuntimeOwnershipOrchestrationTests.HungStop_AbandonedThenLateResult_ConvergesWithoutDoubleCredit`; `ExpeditionController.RunExpedition` |
| H5 start adoption leak | start success + `BeginExpedition` rejection aborts via `ActiveSessionAbort.Abort`: session stopped, base movement returned non-durably | `OperationOwnershipTests.StartAdoptionFailure_AbortStopsSession_MovementReturnsToPassiveStream`; wired in controller, ticker debug path, vehicle path |
| H6 vehicle/debug second transaction path | `UiComposer.VehicleSessionRoutine` delegates result/fault/no-result to the coordinator (trust facts ride along); repo-wide search shows no unsanctioned completion sequence remains | `RuntimeOwnershipOrchestrationTests.VehicleStyleCompletion_*` (marker repair on failed commit, bonus rejected, base kept); grep audit in campaign report |
| H7 rolled-back reward displayed as earned | engine-free `ExpeditionResultPresentation`: positive reward copy only for committed outcomes; reverted → truthful unsaved/retryable copy; fatal → recovery copy only; start cue fires only after real adoption (`StartConfirmed`); audio reapplied from canonical values on revert; permission handler named/detached with owned observations | `RuntimeOwnershipOrchestrationTests.Presentation_*` (3 scenarios); wiring in `ExpeditionController`, `UiComposer`, `FeedbackController.ReapplyCanonicalSettings` |
| M1 permission callback outlives UI | named `OnMotionPermissionStateChanged` detached in `OnDestroy`; refresh/request bounded with `DiscardLateResult` owners | `UiComposer.Compose`/`OnDestroy` |
| M2 audio divergence after rollback | `_feedback.ReapplyCanonicalSettings()` on `PersistenceReverted` | `UiComposer.OnPersistenceReverted` |
| H8 stale nested rollback keys | `ProfileStateCopier.CopyWorldState` prunes target-only building/producer keys inside surviving regions after reuse of surviving instances | `SaveIntegrityApplicationTests.CopyInto_DirtyTarget_RemovesTargetOnlyNestedKeys_AndSerializesExactly` (fails pre-fix, passes post-fix; serialization equality + surviving identities) |
| H9 duplicate dedup entries reopen credited keys | `CreditedActivityKeys.Rebuild()` canonicalization: null/empty removed, duplicates collapsed most-recent-first, capacity applied to unique sequence, membership rebuilt exactly | `DedupCanonicalizationTests` (8 scenarios incl. corruption-across-eviction-boundary failing pre-fix) |

### M8.5 certification matrix

| Gate | Result | Evidence |
| --- | --- | --- |
| Domain suite | PASS | **213/213** (`dotnet test verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj`, this campaign; baseline was 185/185) |
| verify-domain.ps1 | PASS | same suite + restore check, exit 0 |
| Unity static audit | PASS | 107 assets / 107 metas, Unity 6000.3.4f1 pin (`verify-unity-static.ps1`) |
| Release hygiene / privacy audit | PASS | 63 runtime sources scanned, manifest minimal (`verify-release-hygiene.ps1`) |
| Agent guard suites | PASS (ps) | 24/24 ps tier; 12 sh-tier failures are the known WSL `bash.exe` shadowing (see M8.3/M8.4 matrices; ps tier authoritative) |
| Repository identity (live) | PASS | `assert-repo-identity.sh` exit 0 pre-work; re-run before integration |
| `git diff --check` | PASS | clean at final gate run |
| Unity EditMode/PlayMode | UNVERIFIED | licensed editor still blocked (Hub holds zero accounts; licensing client reports "Token not found in cache"); repro: sign into Hub, activate, `scripts/setup-unity-project.ps1`, `scripts/verify-unity-editmode.ps1` / `verify-unity-playmode.ps1`. New Unity-side wiring (ticker/controller/composer) compiles under static gates only |
| Android build/device tiers | UNVERIFIED | Android Build Support absent; provider changes compile under `UNITY_ANDROID && !UNITY_EDITOR` guards; repro: install Build Support + SDK, `scripts/verify-android-smoke.ps1` |
| iOS/Xcode tiers | UNVERIFIED | macOS/Xcode/signing unavailable; iOS teardown change reviewed statically only |

### Deliberate remaining limitations (M8.5)

- A provider task that never completes at all still holds its own single staged window
  (by design: fail-closed, and later windows stay safe). The difference from pre-M8.5 is
  that any task that DOES complete — at any time — has an owner that converges state;
  there is no longer a timing-based stranding window.
- PlayMode-only behaviors (coroutine scheduling under real frame timing, scene
  recomposition, JNI/CoreMotion callback timing) remain UNVERIFIED until a licensed
  editor/device exists; their correctness-critical decisions were extracted engine-free
  and certified headlessly instead.
- No save-schema change: dedup canonicalization repairs existing fields at load;
  claim ids are transient provider-private state.

## M8.6 campaign — Unity first-import & device readiness certification

**Scope (target milestone M8 — Device Ready):** the first real Unity import/compile,
EditMode/PlayMode certification, Android IL2CPP/ARM64 build, deterministic install/
launch/lifecycle smoke, physical step-counter exactly-once, vertical-slice UX, measured
performance, and iOS — plus hardening the certification harness itself so evidence fails
closed and binds to an exact editor/build/device identity.

Start SHA `d48c692ccc745947357fd97850f52fa5f2511215` (prior exec commit); reconciled by
rebasing the implementation branch onto `e78ba78f24e77e7566b9ed3259878f6af83d24b5` (`origin/main`,
the 288-file deep re-audit that added the mandatory R1-R9 / E1-E10 findings). Branch
`agent/walk-game/m8.6-exec-20260826`, single-writer lease `sess-20260826T234929Z-1745-771426137`.

### Environment inventory (this session)

| Capability | State |
| --- | --- |
| OS / shell | Windows 11 / PowerShell 7 |
| .NET SDK | 9.0.300 |
| Unity Hub | present (`C:\Program Files\Unity Hub\Unity Hub.exe`) |
| Unity editor `6000.3.4f1` | **ABSENT** — `C:\Program Files\Unity\Hub\Editor` does not exist; no editor installed |
| Unity license / entitlement | **ABSENT** — `AppData\Local\Unity\licenses` contains only `packages`; no `Unity_lic.ulf`; licensing client shows sessions but no valid entitlement |
| Android Build Support (`AndroidPlayer`) | **ABSENT** (depends on editor install) |
| JDK | 17.0.20 (Adoptium) |
| Android SDK / NDK | `ANDROID_HOME`/`ANDROID_SDK_ROOT` = `C:\Users\palac\AppData\Local\Android\Sdk`; `adb` on PATH |
| Physical Android device | **NONE connected** (`adb devices` empty at run time) |
| macOS / Xcode / signing | **ABSENT** (Windows host) |

This environment is therefore blocked at the editor+license layer for every EDITOR/
DEVICE/iOS lane. The campaign executed every legitimate locally-runnable requirement
and hardened the certification harness, then captured each blocked lane as
`UNVERIFIED — <specific blocker>` per spec E5 rather than simulating a PASS.

### Executed this campaign (AUTOMATED)

| Finding | Fix | Evidence |
| --- | --- | --- |
| F2 EditMode runner overstates success | `scripts/verify-unity-editmode.ps1` now fails closed: requires Unity exit 0 **and** non-empty parseable `editmode-results.xml` with zero failures via shared `Test-NUnitResultXml`; missing/invalid result fails even on exit 0 | `scripts/cert-script-helpers.ps1::Test-NUnitResultXml`; `scripts/Test-CertificationScripts.ps1` (16/16) |
| T2 PlayMode runner only checked artifact existence | `scripts/verify-unity-playmode.ps1` now validates completed-result XML with zero failures, not mere presence | same helpers + tests |
| F3 Android smoke target selection not fail-closed (C1) | `scripts/verify-android-smoke.ps1` adds `-DeviceSerial`, fails early on ambiguity/offline/unauthorized, binds **every** adb command to the chosen serial, records manufacturer/model/release/SDK/ABI/step-counter, APK SHA-256, package id and source SHA; labels emulator/no-step-counter runs `lifecycle-only`; preserves logcat/summary on failure (C3) | `Select-AndroidTarget` / `Get-AndroidDeviceMetadata` / `Get-FileSha256` + tests |
| F3.4 / §3.4 deterministic script regression | new `scripts/Test-CertificationScripts.ps1` (engine-free, no Unity/adb/device) covering result-XML parsing (NUnit2+NUnit3, pass/fail/malformed/missing/no-tests) and target selection (single/multi/preferred/absent/offline/empty) | **16/16 PASS** |
| R4 Editor toolchain identity preflight | new `Get-UnityPinnedVersion` / `Test-UnityEditorMatchesPin` in `cert-script-helpers.ps1`; wired into `verify-unity-editmode.ps1` and `verify-unity-playmode.ps1` so a wrong/unpinned editor fails closed before launch | helpers + `Test-CertificationScripts.ps1` (R4 cases) |
| R6 Idempotent clean-install uninstall | new `Uninstall-AndroidPackageIdempotent` (absent = clean success; still-installed removal failure = real failure); used for pre-install and final cleanup in `verify-android-smoke.ps1` | helpers + `Test-CertificationScripts.ps1` (R6 cases) |
| R7 try/finally summary discipline | `verify-android-smoke.ps1` restructured to write summary JSON + logcat in a `finally` block after optional uninstall, recording `finalDisposition` | `Test-CertificationScripts.ps1` (R7 structural) |
| R17.2.10 foreground/resumed launch evidence | new `Get-AndroidForegroundActivity`; smoke fails if the expected package is not the foreground/resumed activity, not merely process-alive | helpers + `Test-CertificationScripts.ps1` (R17.2.10 cases) |
| R5 adb serial-binding audit | confirmed every direct adb call (`pidof`, `logcat`, `am`, `pm`, `settings`, `input`) routes through the serial-bound `Invoke-Adb` | `Select-AndroidTarget` + tests |

### M8.6 certification matrix

| Gate | Result | Evidence |
| --- | --- | --- |
| Repository identity (live) | PASS | `Assert-RepoIdentity.ps1` exit 0 pre-work and before integration |
| Domain suite | PASS | **213/213** (`dotnet test`, fresh this campaign) |
| verify-domain.ps1 | PASS | same suite + restore check, exit 0 |
| Unity static audit | PASS | 107 assets / 107 metas, Unity 6000.3.4f1 pin (`verify-unity-static.ps1`) |
| Release hygiene / privacy audit | PASS | 63 runtime sources scanned, manifest minimal (`verify-release-hygiene.ps1`) |
| Agent guard suites | PASS | **36/36** (ps + sh + hook tiers; `Test-AgentGuards.ps1`) |
| Certification-script regression | PASS | **35/35** (`scripts/Test-CertificationScripts.ps1`, covering R4/R6/R7/R17.2.10 + parse-only checks) |
| `git diff --check` | PASS | clean at final gate run (CRLF normalization only) |
| Unity import/compile (U1–U6) | **UNVERIFIED** | no Unity `6000.3.4f1` editor installed; repro: install pinned editor, sign into Hub, activate, `scripts/setup-unity-project.ps1`, then `verify-unity-editmode.ps1`. Planner-predicted `WalkGameEditorTools.cs` namespace references (F1) could not be reproduced without an editor — left as predicted, not claimed fixed. |
| EditMode (T1/T3/T4) | **UNVERIFIED** | same editor/license blocker |
| PlayMode (T2/T3/T4) | **UNVERIFIED** | same editor/license blocker |
| Android IL2CPP/ARM64 build (A1–A4) | **UNVERIFIED** | Android Build Support (`AndroidPlayer`) absent (editor not installed); repro: install Build Support + SDK/NDK, `scripts/build-android-development.ps1` |
| Android lifecycle smoke (L1–L5) | **UNVERIFIED (script ready)** | hardened `verify-android-smoke.ps1` rewritten this session with R6 idempotent uninstall, R7 `finally` summary, and R17.2.10 foreground-activity evidence; parse-checked + engine-free tested; no APK/device to run it against |
| Physical step sensor (P1–P11) | **UNVERIFIED** | no physical device exposing `android.hardware.sensor.stepcounter`; no connected adb target |
| Vertical-slice UX (V1–V5) | **UNVERIFIED** | no device |
| Performance/battery/thermal (F1–F6) | **UNVERIFIED** | no device; no profiling target |
| iOS (I1–I7) | **UNVERIFIED** | no macOS/Xcode/signing/device |

### Deliberate remaining limitations (M8.6)

- Every EDITOR/DEVICE/iOS gate stays UNVERIFIED by a precise environment blocker (no
  licensed Unity editor, no Android Build Support, no physical device, no macOS), not by
  missing repository work. The planner-predicted `WalkGameEditorTools.cs` compile issues
  (F1) were neither reproduced nor fixed because doing so would require a licensed editor;
  they remain predicted findings, honestly uncertified.
- The certification harness itself is now fail-closed and identity-binding, so any future
  licensed run will produce trustworthy evidence rather than a false PASS.
- This session additionally closed the re-audit's script-level findings: R4 editor-identity
  preflight, R6 idempotent uninstall, R7 `finally` summary persistence, and R17.2.10
  foreground/resumed launch evidence are implemented and locked by engine-free tests (35/35),
  so the harness is now strictly fail-closed end-to-end for those tiers. Their *runtime*
  enforcement remains UNVERIFIED only because the licensed editor / device is absent.

### Next-campaign recommendation

Because the dominant M8 risk (real Unity + Android device readiness) could not be retired
in this environment, the next campaign should still target **M8 Device Ready / M9 Closed
Playtest Readiness** — but it must run on a host with a licensed Unity editor and, ideally,
a physical step-counter Android device. If a measured exactly-once or performance blocker
emerges only under a real editor/device, that measured blocker should drive a focused
follow-up campaign.

---

## M8.7 Canonical State & Certification Integrity Closure

**Status:** COMPLETE (all locally executable requirements closed; EDITOR/DEVICE/iOS tiers remain UNVERIFIED by the same environment blocker, not by missing repository work).
**Planned-From:** `main@e78ba78` (authoritative main at campaign start).
**Planner branch:** `agent/walk-game/m8.7-planner-20260827`.
**Implementation branch:** `agent/walk-game/m8.7-exec-20260827`.
**Milestone:** M8 — Device Ready.

### Prior-session recovery (M8.6)

- The prior M8.6 executor commit `d0c8687` was stranded off-origin because the tracked
  pre-push guard refused the first push of a branch whose remote ref did not yet exist.
- During this campaign the remote branch `agent/walk-game/m8.6-exec-20260826` (created
  by the planner at `e78ba78`) was confirmed an ancestor of the local M8.6 branch, so the
  stranded commit was pushed **normally** (fast-forward `e78ba78..d0c8687`) after the
  remote-advance guard passed. No force-push was used.
- The M8.6 harness work (R4/R6/R7/R17.2.10 fail-closed certification evidence, 35/35
  `Test-CertificationScripts`) was merged into the M8.7 implementation branch so M8.6
  equivalent fixes are preserved and verified, not assumed.

### H1 — null current RegionState could crash boot (CLOSED)

`WorldState.GetOrCreateRegionState` now self-heals an existing key whose value is null
(H3/S3). `SaveValidator.RepairAndValidate` reconstructs a required null region (current
or unlocked) from the authoritative key, or prunes an unreachable null entry. Boot-equivalent
access (`EnsureRegionState` → `GetOrCreateRegionState`) no longer throws.

- Regression: `M87SaveIntegrityClosureTests.H1_*` (3 cases).
- `SaveValidationReport.ReconstructedNullRegionStates` / `PrunedUnreachableNullRegionStates` record counts.

### H2 — region key / RegionState.regionId split identity (CLOSED)

After repair, each surviving `regionStates` entry has a non-null `RegionState` whose
`regionId` matches the dictionary key. The key is the authoritative storage identity; a
conflicting `regionId` is normalized (no progression invented).

- Regression: `M87SaveIntegrityClosureTests.H2_*` (2 cases) + `H2_NoSplitIdentityDownstream`.
- `SaveValidationReport.NormalizedRegionIdentityMismatches` records counts.

### H3 — null VitalityTransaction could crash rollback (CLOSED)

`SaveValidator` prunes null `recentVitalityTransactions` elements and reports the count
(`PrunedNullTransactions`). `ProfileStateCopier.CopyInto` additionally skips null
history elements as a defense-in-depth rollback-boundary guard. A failed
`PersistenceCoordinator.Commit` driven by a durable save containing a null transaction
converges to `RevertedToLastKnownGood` without an unhandled exception; balance and valid
entries are preserved, no Vitality is minted.

- Regression: `M87SaveIntegrityClosureTests.H3_*` (3 cases).

### H4 — structural invariant matrix (CLOSED)

Every serializer-visible persisted family is now covered by an explicit regression
(P6/S6): `PlayerProfile` root refs, `WorldState` current/unlocked/region map, `RegionState`
sets/maps + building placement, `BuildingState`, `ProducerState`, `ActivitySyncState` dedup
(rebuilt; `activeSession` legitimate null preserved), `AchievementState`, `PlayerSettings`,
and `VitalityTransaction` list elements. `S7` confirms repair never mints progression.

- Regression: `M87SaveIntegrityClosureTests.H4_StructuralInvariantMatrix_AllFamiliesClassified`
  and `S7_RepairDoesNotMintProgression`.

### H5 — first-push guard deadlocked on new branches (CLOSED)

`.githooks/pre-push`, `scripts/check-remote-advance.sh` and `scripts/Check-RemoteAdvance.ps1`
now distinguish three states: (1) exact ref exists → fetch and require it an ancestor;
(2) exact ref is positively absent → allow the first normal push; (3) origin unqueryable
(transport/auth) → fail closed. No force path, no deletion path was opened.

- Scenario matrix (engine-free, local bare fixtures): contained remote → OK; unexpected
  advancement → refuse; **first push to absent branch → allowed**; similarly-named branch
  does not satisfy the exact ref; unreachable origin → fails closed. The PowerShell twin
  (`Check-RemoteAdvance.ps1` + pre-push hook) passes every scenario. The real-repository
  `sh scripts/check-remote-advance.sh` run on the new `agent/walk-game/m8.7-exec-20260827`
  branch returned exit 0 (new-branch allowed) in genuine bash.
- Regression: `scripts/Test-AgentGuards.ps1` H5 scenarios (S11c/S11d/S11e/S11h/S11i).

### M8.7 certification matrix

| Gate | Result | Evidence |
| --- | --- | --- |
| Repository identity (live) | PASS | `Assert-RepoIdentity.ps1` / `assert-repo-identity.sh` exit 0 before mutation and integration |
| Domain suite | PASS | **224/224** (`dotnet test`; 213 baseline + 11 new M8.7 regressions) |
| verify-domain.ps1 | PASS | same suite + restore check, exit 0 |
| Unity static audit | PASS | **108 assets / 108 metas**, Unity 6000.3.4f1 pin (added `M87SaveIntegrityClosureTests.cs.meta`) |
| Release hygiene / privacy audit | PASS | 63 runtime sources scanned, manifest minimal |
| Agent guard suites — PowerShell twin | PASS | **all** scenarios including H5 first-push/advancement/unreachable/similar-branch + hook force/delete/unreachable |
| Agent guard suites — sh twin | **ENV-BLOCKED** | pre-existing sandbox limitation: the `Git Bash` on PATH cannot `cd` into the working/temp paths in this environment (the very first suite run already showed `[sh] S1` failing before M8.7 changes). H5 logic is proven via the PowerShell twin and the real-repo `sh` check-remote-advance run; no repository defect is open. |
| Certification-script regression | PASS | **35/35** (`scripts/Test-CertificationScripts.ps1`, M8.6 harness preserved) |
| `git diff --check` | PASS | clean (CRLF normalization only) |
| Unity import/compile (U1–U6) | **UNVERIFIED** | no licensed Unity `6000.3.4f1` editor installed |
| EditMode / PlayMode | **UNVERIFIED** | same editor/license blocker |
| Android IL2CPP/ARM64 build | **UNVERIFIED** | Android Build Support absent (editor not installed) |
| Android lifecycle smoke | **UNVERIFIED (script ready)** | hardened `verify-android-smoke.ps1` in place + engine-free tested; no device/APK to run |
| Physical step sensor / UX / performance / iOS | **UNVERIFIED** | no device / no macOS/Xcode/signing |

### Deliberate remaining limitations (M8.7)

- The only open gaps are the EDITOR / DEVICE / iOS tiers, blocked by the identical
  environment absence (licensed Unity editor, Android Build Support, physical step-counter
  device, macOS/Xcode/signing). No canonical-state, persistence, rollback, or guard
  Critical/High defect remains. The M8.7 first-push deadlock that stranded M8.6 is itself
  fixed.
- The `sh` column of `Test-AgentGuards.ps1` is environmentally blocked in *this* sandbox
  (documented above); it is not a regression of M8.7 work. On a host where Git Bash can
  address the working tree, all `sh` scenarios (including H5) are expected to pass.

### Next-campaign recommendation

Because all discovered Critical/High canonical-state, integration, and guard-integrity
findings are now closed and no executed tier exposes a release blocker, recommend
**M9 Closed Playtest Readiness / Validation** — to be executed on a host with a licensed
Unity `6000.3.4f1` editor and, ideally, a physical step-counter Android device. If a
measured exactly-once, performance, build, or UX blocker emerges only under real
hardware, that measured blocker should drive a focused follow-up campaign rather than a
broad M9 expansion.

## M8.8 re-audited completion evidence — 2026-08-28

This is the current campaign record. The older M8/M8.5/M8.6/M8.7 sections above are
historical snapshots and retain their original counts for traceability.

### Fresh local gates

| Gate | Result | Evidence / limitation |
| --- | --- | --- |
| Repository identity | VERIFIED PASS | `scripts/Assert-RepoIdentity.ps1` exit 0 before mutation; exact repository `quantdale/walk-game` |
| Domain suite | VERIFIED PASS | `dotnet test verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj`: **263/263** |
| `verify-domain.ps1` | VERIFIED PASS | Same current source and standalone restore/test gate |
| Unity static audit | VERIFIED PASS | `scripts/verify-unity-static.ps1`: **112 assets / 112 metas** |
| Release hygiene | VERIFIED PASS | `scripts/verify-release-hygiene.ps1`: **63 runtime C# sources** |
| Agent guards | VERIFIED PASS | `scripts/Test-AgentGuards.ps1`: **43 passed, 0 failed**; real-hook reasons are asserted |
| Certification-script fixtures | VERIFIED PASS | `scripts/Test-CertificationScripts.ps1`: **71 passed, 0 failed** |
| Diff whitespace | VERIFIED PASS | `git diff --check` clean |
| Required remote CI | PENDING FINAL PUBLICATION | Must be checked against the final pushed SHA; a local green run is not a CI claim |

### Finding disposition

| Finding | Disposition | Current evidence |
| --- | --- | --- |
| H0 guard fixture / weak hook reasons | CLOSED | Fixture uses local push transport without rewriting canonical origin; S11f/g/h/i assert intended rejection/allow reasons; 43/43 guard suite |
| H1 Unity editor namespaces | SOURCE FIXED; EDITOR UNVERIFIED | `UnityEngine.Rendering` and `UnityEditor.Build` imports are present; no Unity executable/license on this host |
| H2 SaveMigrator false success | CLOSED HEADLESS | Minimum/current schema policy, exact +1 migration state machine, repository fail-closed load tests in `M88SaveMigratorContractTests`; 263/263 suite |
| H3 semantic Unity compile evidence | HARNESS CLOSED; EDITOR UNVERIFIED | `verify-unity-compile.ps1` and false-green fixtures reject launch/compiler/stale/missing-completion/mutation cases; no editor available |
| H4 generated project state | UNVERIFIED EXTERNAL | No manual opaque Unity assets were added; first-import/package-lock/project/URP materialization and second clean-checkout proof require licensed Unity |
| H5 Android target | SOURCE/HARNESS CLOSED; BUILD UNVERIFIED | Editor target is API 36, wrapper checks generated APK `targetSdkVersion` with `aapt`; AndroidPlayer/IL2CPP and APK are unavailable |
| P1 Android permission restart | HEADLESS STATE COVERAGE CLOSED; DEVICE UNVERIFIED | `M88AndroidPermissionStateTableTests`; no connected `adb` target for lifecycle/restart proof |
| P2 iOS provider lifetime | SOURCE COVERAGE CLOSED; AOT/DEVICE UNVERIFIED | Rooted callback delegate, pending cancellation, provider generations, and source-level tests; no macOS/Xcode/iOS device |
| M1 spend reason | CLOSED | Empty/null reason rejected before balance mutation; focused tests |
| M2 arithmetic | CLOSED | Score/resource additions saturate and clamp; boundary tests |
| M3 material creation | CLOSED HEADLESS | Null `Shader.Find` results no longer reach `new Material`; Unity visual/import behavior unverified |

### Tool and hardware inventory

- Unity editor executable/license: unavailable; `ProjectSettings/ProjectVersion.txt` remains
  pinned to `6000.3.4f1`.
- Android SDK: platform `android-36` and build-tools `36.0.0` present under the local SDK;
  Unity Android Build Support/IL2CPP is not installed.
- Java: Temurin/JDK 17 available. `adb` is installed, but `adb devices -l` has no target.
- Apple lane: Windows host; no macOS, Xcode, iOS SDK, signing environment, or iOS device.

M8.8 is complete only at the implementation/evidence tier permitted by these facts. The
repository is not FULL MVP PRODUCTION-CERTIFIED: editor, generated-project, Android APK/
device/sensor, iOS build/device, and measured UX/performance tiers remain UNVERIFIED.
