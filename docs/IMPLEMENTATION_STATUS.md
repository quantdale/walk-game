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

Last updated: 2026-08-26 (M8.4 runtime orchestration durability & headless certification campaign)

## Verification status

- Domain test suite: **185/185 passing (AUTOMATED)** via
  `dotnet test verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj`
  (`scripts/verify-domain.ps1`; also runs in CI on every push/PR to `main`). M8.4 added 20 headless scenarios (165 → 185).
- CI domain gate: configured (`.github/workflows/domain-tests.yml`) now including the
  release-hygiene/privacy audit — **AUTOMATED**.
- Unity `6000.3.4f1` is installed at `C:\UnityEditors\6000.3.4f1\Editor`, its executable
  reports the pinned version, and `Data\Resources\PackageManager\Server\UnityPackageManager.exe`
  is present. M8 re-investigation from scratch confirmed the licensing gate is account-level:
  Unity Hub 3.21.0 (MSIX) runs but holds **zero logged-in accounts** (`accounts.db` empty), the
  licensing client reports "Token not found in cache" with 0 entitlement groups and no ULF,
  and every editor entitlement resolves to `granted: False`. No offline activation path exists
  without user credentials; fabricating or bypassing licensing is prohibited. Unity compile,
  EditMode, and PlayMode therefore remain **UNVERIFIED** (reproducible gate: sign into Hub,
  activate a license, run `scripts/setup-unity-project.ps1`, `scripts/verify-unity-editmode.ps1`,
  `scripts/verify-unity-playmode.ps1`).
- Android Build Support remains absent (`AndroidPlayer` missing; only Windows Standalone is
  installed). A prior Hub module-install attempt is recorded as paused with status
  `install-queued`, `ELEVATION_CANCELLED`; this session cannot elevate. The committed build
  entry point now certifies the release-shaped backend (IL2CPP + ARM64, minSdk 26, targetSdk 35).
  Android build/install/launch and native lifecycle evidence remain **UNVERIFIED**;
  `scripts/verify-android-smoke.ps1` is committed and ready for the first emulator/device.
  iOS Xcode generation/build remains **UNVERIFIED** (no macOS/Xcode).
- Static bring-up audit of assemblies/GUIDs/scenes/packages: **AUTOMATED**; 102 asset files
  and 102 `.meta` files pass the audit with zero missing real GUID references. The only unresolved
  scene GUID is Unity's built-in zero GUID for the authored light. (Counts refreshed by the
  M8.4 campaign; the pre-M8.1 record cited 94/94.)

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
| `ActivityTicker.ReconcileRoutine` rewrite | captures refs before fatal; timeout path drains late completion on the main thread and rejects unprocessed delivery (`durable=false`, cursor untouched) up to a 30 s hard cap; commit path delegates to coordinator; NRE on fatal fixed |
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

- Provider claim/restoration state stays in-memory by design (see ADR 0009); crash recovery derives from persisted cursors plus native absolute/history facts. A provider task that never completes after the 30 s hard cap may leave a claim open until restart — the only unbounded wait; providers that hang forever are provider bugs.
- Fatal persistence loss remains fail-closed per ADR 0007; movement observed inside a fatal window is unrecoverable and no bonus is synthesized.
- Scene composition (`AppFlowController`, `Builder`/`Explore` rig, `UiComposer`) and provider JNI / CoreMotion callbacks remain UNVERIFIED without an editor/device.
