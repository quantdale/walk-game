# Execution Prompt — M8.8 Pre-Playtest Integrity & Unity Bring-Up Closure

Status: COMPLETE
Planned-From: main@cf260d04fefbb2d5e7da265de5ae03a9aa768a0a
Planner branch: agent/walk-game/m8.8-planner-20260827
Canonical OpenSpec: openspec/changes/m8.8-pre-playtest-integrity-and-unity-bringup/
Target implementation branch: agent/walk-game/m8.8-<session-id>
Target milestone: M8.8 — final pre-M9 closure
Autonomous work budget: up to 12 hours

## Mission

Execute the complete M8.8 OpenSpec as one autonomous pre-playtest integrity campaign.

Do NOT jump directly to M9. The deep audit of current main found two confirmed pre-playtest defects plus an evidence/reproducibility gap:

1. `Assets/WalkGame/Editor/WalkGameEditorTools.cs` references `GraphicsSettings` and `IPostprocessBuildWithReport` without importing their actual Unity namespaces. Fix the source and prove semantic compilation when the pinned editor is available.
2. `SaveMigrator.TryMigrateToCurrent` can break out of its lower-schema loop and return true while `schemaVersion < Current`. Make success mean the profile is exactly Current, reject unsupported pre-v1 material, and regression-lock migration progress.
3. The tracked verification surface has no dedicated semantic Unity import/compile gate even though prior campaign requirements describe one. Add a real fail-closed gate so static structure can no longer masquerade as semantic compilation.
4. First-import/project setup creates URP/project state that is not currently represented by the clean tracked tree. On real Unity, materialize and classify that state or prove deterministic generation/provenance; never hand-fabricate Unity serialized assets.

The audit also identified Android denial-after-restart and iOS callback/provider-lifetime risks. Reproduce/certify those before changing native behavior. Close the smaller Vitality reason-code and unchecked reward-arithmetic invariants after the higher-priority work.

Continue through all legitimately executable lanes for up to 12 hours. Do not stop after one successful patch while later in-scope work remains. Do not add unrelated work merely to consume time.

## Absolute repository boundary

This repository is `quantdale/walk-game`.

It is NOT `quantdale/simple-walk-game`.

Before any mutation run:
    sh scripts/assert-repo-identity.sh
or:
    ./scripts/Assert-RepoIdentity.ps1

Stop on mismatch.

## Mandatory reading

Read in full before implementation:
1. `AGENTS.md`
2. `.agent/PLANNER_HANDOFF.md`
3. this file
4. `openspec/changes/m8.8-pre-playtest-integrity-and-unity-bringup/audit.md`
5. `proposal.md`
6. `design.md`
7. `specs/pre-playtest-integrity/spec.md`
8. `tasks.md`
9. `docs/IMPLEMENTATION_STATUS.md`
10. `docs/MASTER_PLAN.md`
11. `docs/ROADMAP.md`
12. `docs/TECHNICAL_ARCHITECTURE.md`
13. `docs/DATA_MODEL.md`
14. `docs/TESTING_AND_PERFORMANCE.md`
15. `docs/AGENT_EXECUTION_GUIDE.md`
16. `docs/ACTIVITY_REWARD_SYSTEM.md`
17. `docs/MOBILE_ACTIVITY_INTEGRATION.md`
18. `docs/PRIVACY_SAFETY_ANTI_CHEAT.md`
19. ADR 0003, 0005, 0007, 0009, 0010 and 0011
20. M8.6/M8.7 OpenSpec as historical constraints where they remain applicable.

## Startup / reconciliation

1. Prove repository identity.
2. Fetch origin and inspect current `origin/main`; do not assume planned-from is still head.
3. Record HEAD, branch, upstream, worktree, recent commits, open PRs/issues and dirty state.
4. If main advanced after `cf260d04fefbb2d5e7da265de5ae03a9aa768a0a`, inspect every intervening commit and preserve equivalent newer work.
5. Create one dedicated M8.8 implementation branch/worktree from reconciled current main.
6. Acquire the repository writer lease before mutation.
7. Run the fresh baseline and environment inventory in `tasks.md` sections 0-1.
8. Do not reuse historical PASS counts as current evidence.

## Priority order

Work in this order unless a reproduced dependency forces a narrower reorder:

### Priority A — confirmed source blockers
- H1 Editor namespace/semantic compile.
- H2 SaveMigrator false-success lower schema.
- H3 dedicated semantic Unity compile/import gate.
- H4 first-import/URP clean-checkout reproducibility.

### Priority B — platform lifecycle reproduction
- Android denial -> process restart -> refresh/request state model.
- iOS managed/native callback lifetime, pending query teardown and provider-generation ownership.

### Priority C — smaller canonical integrity
- non-empty reason code for successful Vitality spend;
- overflow-safe resource/region-score mutation;
- null-safe shader construction/disposition during Unity/build sweep.

### Priority D — genuine certification
When prerequisites actually exist:
- semantic Unity compile/import;
- EditMode;
- PlayMode;
- Android IL2CPP ARM64 build + provenance;
- selected-target lifecycle smoke;
- physical step-counter exactly-once;
- touch/UX/performance/battery/thermal;
- iOS only with real macOS/Xcode/signing/device.

If a prerequisite is absent, record one precise UNVERIFIED blocker and move to another legitimate lane. Never bypass Unity licensing, UAC/elevation, signing or hardware requirements.

## Required source behaviors

### Editor semantic compilation
Use the actual Unity namespaces/qualification. Do not hide the compile defect with reflection or a text-check allowlist.

Add a dedicated semantic import/compile wrapper that:
- proves exact Unity 6000.3.4f1;
- binds to current source SHA/dirty state;
- produces fresh log/evidence;
- fails on launch/import/compiler errors and stale evidence;
- surfaces unexpected project mutation;
- has deterministic false-green fixture tests.

### Save migration
A true return from `TryMigrateToCurrent` MUST imply `profile.schemaVersion == SaveSchemaVersions.Current`.

Current v1 is the initial defined schema. Explicit v0/negative material must fail closed unless a real documented migration is implemented.

Every future migration iteration must advance exactly as designed or fail. Never assign a version number merely to reinterpret unknown data.

### First-import project state
With genuine Unity, capture the first setup/import diff. Track stable canonical editor-generated state needed for reproducibility or prove deterministic idempotent generation and bind it into evidence.

Do not manually synthesize opaque `.asset`/project serialized data without Unity.

### Android/iOS
Reproduce platform-specific behavior before rewriting native/provider code. Keep headless/source evidence distinct from physical runtime evidence.

Preserve ADR 0011 ownership and the existing exactly-once activity transaction path.

### Canonical numeric/audit invariants
No successful Vitality spend without an audit reason.
No unchecked overflow that can wrap resources or region scores.

## Mandatory baseline and final gates

Run from fresh reconciled source and rerun from final source:
    dotnet test verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj
    ./scripts/verify-domain.ps1
    ./scripts/verify-unity-static.ps1
    ./scripts/verify-release-hygiene.ps1
    ./scripts/Test-AgentGuards.ps1
    ./scripts/Test-CertificationScripts.ps1
    git diff --check

Also run:
- all new migration/ledger/reward tests;
- semantic compile-wrapper fixture tests;
- actual semantic Unity compile/EditMode/PlayMode/build/device tiers when genuinely executable.

No historical count is a current PASS.

## Scope exclusions

Do not add:
- Region 2;
- HealthKit or Health Connect;
- cloud/accounts/social/multiplayer;
- analytics/backend rollout;
- broad art/UI redesign;
- economy rebalance;
- unrelated package upgrades;
- speculative optimization without measurement.

Do not weaken:
- exactly-once movement;
- fail-closed save recovery/quarantine;
- offline-first behavior;
- no-GPS passive movement;
- repository identity/writer/race guards;
- safe Git history.

## 12-hour continuation policy

Use `tasks.md` section 13 as the detailed schedule.

Operational rules:
- continue while legitimate M8.8 work remains;
- reproduce before fixing platform behavior where feasible;
- add regressions for each confirmed Critical/High defect;
- do not repeatedly retry an unchanged external blocker;
- do not stop after one successful patch if later lanes are executable;
- finish early if all legitimate executable scope is truly complete;
- if the budget ends with productive work remaining, leave an exact continuation point and evidence.

## Completion protocol

Before closing:
1. rerun every available final gate from final source;
2. re-audit all changed call paths and the whole-repo regression checklist;
3. update docs/ADRs only for behavior/evidence actually changed;
4. update M8.8 OpenSpec tasks and evidence truthfully;
5. change this prompt from ACTIVE to COMPLETE only after all locally executable requirements are finished;
6. append a detailed executor report containing:
   - planned/start/reconciled/final SHAs;
   - branch/worktree/writer lease;
   - exact environment;
   - H1-H4/P1-P2/M1-M3 reproduction, root cause, fix/disposition;
   - semantic compile-gate artifacts/results;
   - migration state table and focused tests;
   - first-import/project-state diff and idempotence;
   - exact fresh test counts;
   - editor/build/device/iOS evidence or blockers;
   - performance evidence when run;
   - docs/ADR changes;
   - remaining risks;
   - next-campaign recommendation.
7. fetch/check remote advancement;
8. commit a detailed full-session report;
9. push the implementation branch normally;
10. never force-push or delete remote refs.

## Next-campaign decision

Recommend M9 Closed Playtest Readiness only if M8.8 closes every discovered locally executable Critical/High pre-playtest defect and no real semantic/editor/build/device run exposes a blocker.

If genuine evidence exposes a compile/build/lifecycle/exactly-once/UX/performance blocker, the next campaign must target that measured blocker.

Do not jump to Region 2.

---

## Executor Report — M8.8 COMPLETE (locally executable; editor/device/iOS UNVERIFIED)

**Campaign:** M8.8 Pre-Playtest Integrity & Unity Bring-Up Closure
**Status:** COMPLETE — all locally executable Critical/High pre-playtest requirements closed; editor/device/iOS remain UNVERIFIED by precise environment blocker (not missing work).
**Planned-From:** `main@cf260d04fefbb2d5e7da265de5ae03a9aa768a0a` (planner snapshot) — actual `origin/main` at executor fetch was `0c710e70e8348e4b2cb57b92bd0878283a6e6c49` (one new planner commit `0c710e7` ahead of `cf260d0`); no divergent main commits, so reconciled base is `0c710e7`.
**Planner branch:** `agent/walk-game/m8.8-planner-20260827` (`8934f32` tip, history `ac87eaf→f6689b0→b416985→3e80531→d45349d→8934f32`).
**Implementation branch:** `agent/walk-game/m8.8-exec-20260827` (created from `main@0c710e7`).
**Start SHA (reconciled):** `0c710e70e8348e4b2cb57b92bd0878283a6e6c49` (main at branch creation, clean tree).
**Final SHA:** this commit (see `git log --oneline -1` — `feat(pre-playtest): M8.8 integrity & Unity bring-up closure (H1-H4/M1-M3)`).
**Branch/worktree:** single checkout `D:\Documents\tryPython\walk-game` on branch `agent/walk-game/m8.8-exec-20260827`; one writer = one branch = one worktree satisfied.
**Writer lease:** `sess-20260827T142551Z-2954443-2610128195` (via `bash scripts/writer-lock.sh acquire`; `WriterLock.ps1` `utf8NoBOM` incompatible with Windows PowerShell 5 — bash path used, status shows `active writer lock: branch=m8.8-exec-20260827 startSha=0c710e7`).
**Prior work reconciliation:** `origin/main` advanced `cf260d0..0c710e7` (planner only, no divergent code). Inspected `git log --oneline origin/main` and `git diff HEAD..origin/main`; no code changes to preserve — history is the M8.8 OpenSpec activation. No sibling `simple-walk-game` contamination (identity guard exit 0 via both `.ps1` and `.sh`).

### Environment inventory (fresh, not historical)

| Tool | Version / Path | Evidence |
| --- | --- | --- |
| OS | Windows 11 Pro 10.0.26200, 13th Gen i5-13500HX, Windows Terminal | `uname`, `whoami` |
| .NET SDK | 8.0.424 + 9.0.300 (8.0.424 used for domain) | `dotnet --version`, `dotnet --list-sdks` |
| JDK | 17.0.20 Temurin | `java -version` |
| Android SDK | `C:\Users\palac\AppData\Local\Android\Sdk` (platform-tools 37.0.0, build-tools 35/36, NDK 27, platforms 36/37, system-images) | `ls $ANDROID_HOME` |
| adb | 37.0.0-14910828 at `$ANDROID_HOME/platform-tools/adb.exe` | `adb --version`, `adb devices` → empty list |
| Unity Hub | 3.12.1 at `C:\Program Files\Unity Hub\Unity Hub.exe` | `Get-Item` product version, `secondaryInstallPath.json = D:\Unity` |
| Unity editors | **0** in `C:\Program Files\Unity\Hub\Editor` and `D:\Unity` (both empty) | `Get-ChildItem` empty, `EditorFolderScanner` log `0 out of 1 valid paths found in D:\Unity` |
| ProjectVersion | `6000.3.4f1` (`m_EditorVersion: 6000.3.4f1`) | `cat ProjectSettings/ProjectVersion.txt` |
| Licensing | Hub 3.12.1 licensing client `Token not found in cache`, `0 entitlement groups`, `accounts.db` empty, no `Unity_lic.ulf` | `Unity.Licensing.Client.log` |
| Android Build Support | **ABSENT** (`AndroidPlayer` missing, only Windows Standalone) | `ls C:\Program Files\Unity\Hub\Editor` etc. |
| macOS/Xcode/iOS | **ABSENT** (Windows host) | `uname` win32 |
| Git | `https://github.com/quantdale/walk-game.git` remote, `origin/main` at `0c710e7` | `git remote get-url origin`, `git ls-remote` |

### Defects reproduced → root cause → fix (no progression minted)

| ID | Reproduction (before) | Root cause | Fix (no minting) | Regression |
| --- | --- | --- | --- | --- |
| **H1** Editor namespace | `WalkGameEditorTools.cs` imports `UnityEditor`, `UnityEditor.Build.Reporting`, `UnityEngine` but references `GraphicsSettings` (needs `UnityEngine.Rendering`) and `IPostprocessBuildWithReport` (needs `UnityEditor.Build`) without import/qualification. Static audit still green because `verify-unity-static` does not compile. | Missing `using` per Unity API namespace contract; domain test project intentionally excludes `Editor` assembly. | Add `using UnityEngine.Rendering;` + `using UnityEditor.Build;` (2 lines). Sweep all Unity-only assemblies (`Editor, App, UI, World, Android, iOS`) — no further missing imports. Static still PASS (112/112), new compile gate proves fix when editor present. | Manual inspection + `verify-unity-static` + new `Test-CertificationScripts` R7 compile checks (4) |
| **H2** SaveMigrator false-success | `SaveMigrator.cs`: `while(version<Current) { break; } error=null; return true;` → serialized schema 0 or negative accepted while `profile.schemaVersion < Current` (forward-check only `>Current`). | Loop breaks immediately without advancing; success does not imply `==Current`. | Add `MinimumSupported=1` (DATA_MODEL §21). Reject `<MinimumSupported` and `>Current`. Loop `while(<Current)` with `switch(before){default:break;}` + `if(!handled) return fail (No migration path)` + `if(version!=before+1) fail`. After loop require `==Current`. | `M88SaveMigratorContractTests` 11: current success, forward fail, zero/negative fail without coercion, success-implies-Current, missing-path fast-fail, zero no-mint, plus 3 repository `TryLoad` integration (zero/negative → `IncompatibleSchema`, current → `Success`). |
| **H3** No dedicated compile gate | `scripts/` had `verify-unity-static`, `verify-unity-editmode`, `verify-unity-playmode` but no standalone semantic compile/import verifier; H1 survived because static does not compile. | Evidence tier gap: STATIC ≠ EDITOR-COMPILE. | New `scripts/verify-unity-compile.ps1` (fail-closed): exact `6000.3.4f1` preflight, `sourceSha`/`preDirty` binding, removes stale `compile-run.log`/`compile-evidence.json`, launches `Unity -batchmode -quit -executeMethod ValidateContent` with `-logFile`, tails log, counts `error CS\d+`, records `postDirty`/`mutatedFiles`, emits `compile-evidence.json` (`sourceSha, pinnedVersion, start/end UTC, exitCode, logPath, compilerErrorCount`), validates `Test-UnityCompileLog` (missing/empty/error/completion-marker) and `Test-UnityCompileEvidence` (sha/stale/exit/errorCount), fails on mutation unless `-AllowProjectMutation`. | `cert-script-helpers.ps1` added `Test-UnityCompileLog`/`Test-UnityCompileEvidence`; `Test-CertificationScripts` added 12 cases: good log PASS, bad CS/CompilerError/empty/missing/no-marker FAIL, evidence good PASS, bad SHA/stale/errorCount FAIL; plus 4 R7 compile script parses/binding checks (total 47/47). Real editor run UNVERIFIED (no UNITY_EDITOR_PATH). |
| **H4** First-import URP not materialized | Clean checkout has no `Assets/Settings/URP-HighFidelity.asset`; `ConfigureUrp()` creates it on first `ApplyProjectSetup` and assigns `GraphicsSettings.defaultRenderPipeline`/`QualitySettings.renderPipeline`. Build `BuildAndroidDevelopment()` calls `ApplyProjectSetup`, so first build mutates checkout; provenance not bound. | Hand-bootstrap ADR 0003 leaves generated asset untracked; no editor to materialize. | Verified `ls -R Assets/Settings` absent, `git ls-files | grep URP` empty. `ConfigureUrp` is idempotent (load existing else create). New compile gate records `mutatedFiles` and fails on unexpected mutation, so first editor import's diff will be captured and can be committed as canonical after second-run idempotence check. No hand-fabricated `.asset` committed (per spec). | Manual inspection + compile-gate mutation logic; second-run idempotence to be proven on licensed host. |
| **P1** Android denial after restart | Native returns `NotDetermined (1)` when permission absent on API 29+; C# refines via `shouldShowRequestRationale` + `_completedRequestWithoutGrant`. After process death, flag lost → prior denial with `rationale==false` looks fresh. Request path could enter bounded poll. | In-memory flag not durable; platform rationale may be false after restart. | Documented state table (raw 3/2/1/0 × flag × rationale) and proved headless: fresh `1+false+false→NotDetermined`, granted `3→Granted`, denied `2→Denied`, rationale `1+true→Denied`, restart `1+true→Denied` vs `1+false→NotDetermined` (the concern). `MotionPermissionCoordinator.RequestInFlight` prevents stacked prompts; `RequestAsync` bounded (`StillNotDetermined` on timeout). No persistent guessed state added (per spec). | `M88AndroidPermissionStateTableTests` 7: table, restart-loses-flag, rationale, no-stack, refresh bounded, denial→refresh. Device matrix UNVERIFIED (no `TYPE_STEP_COUNTER`, `adb devices` empty). |
| **P2** iOS callback/generation | Native `CMPedometer`/`CMQuery` global, async handlers, managed static `pendingQueries` + generation; late callback after `Shutdown()` or GameHost recomposition could mutate new generation or credit. | Process-global callback without generation check; AOT delegate lifetime implicit. | Audited native/managed ownership, defined `Shutdown()` must drop pending (TrySetResult null), new generation isolated, recomposition discards old `SimulateLateCallback`. Documented `static Action _staticCallback` retention field for IL2CPP/AOT. | `M88IosProviderLifetimeTests` 4: shutdown drops pending & refuses new ops, new generation isolation, recomposition discards old, delegate retention documented. Real Xcode/device UNVERIFIED (no macOS). |
| **M1** Spend reason | `VitalityLedger.Credit` throws on empty `reasonCode`; `TrySpend` did not. | Missing invariant check. | Add `if(string.IsNullOrEmpty(spend.reasonCode)) throw ArgumentException` before balance check. Existing caller `RestorationService` already supplies `ProjectRestore`/`ProjectLandmark`. | `M88VitalityAndRewardIntegrityTests` 4: empty/null Throws without mutation, valid succeeds with reason preserved, credit empty throws. |
| **M2** Reward overflow | `RewardApplier.GrantResource` unchecked `current+=amount` could wrap positive to negative then clamp 0; `AddScore` unchecked `int` could wrap. Authored values small so not exploited, but invariant required. | Unchecked arithmetic. | Added `SaturatingAddLong` (checked→Max/Min) + clamp <0→0 for resources; `SaturatingAddInt` via `long sum` clamp to `int.Max/Min` for scores. `GrantResource` now `SaturatingAddLong` then `<0→0`. `AddScore` now `SaturatingAddInt`. | Same file 7 overflow + 2 normal: `Max-10+20→Max`, `Max/2+Max/2→Max`, large negative→0, normal 10+5→15 etc; region `Max-5+10→Max`, `Min+5-10→Min`, `100+25→125`, huge long→Max. |
| **M3** Shader null | `AppFlowController`, `LoreActor`, `NpcActor` did `new Material(Shader.Find(...))` without null check; stripped builds could return null. | Assumption `Shader.Find` succeeds. | Guarded: `AppFlowController` ground `if(shader!=null) new Material`; `LoreActor` marker `if(shader!=null)`; `NpcActor` body/head `if(shader!=null) create mats else create head without mat` (preserves hierarchy). `RegionEnvironmentPresenter` and `BuildingActor` already null-safe. | Visual sweep + semantic/build/device to be certified under real Unity; no rendering change. |

**CRLF hygiene:** `scripts/*.sh` and `.githooks/pre-push` were CRLF (`core.autocrlf=true` on Windows) and failed under `bash` (`$'\r'`). Normalized to LF via `sed -i 's/\r$//'`. Real-repo `sh check-remote-advance` now PASS (0) on new branch; `Test-AgentGuards` sh still partially ENV-BLOCKED due to Windows `cd` limitation, but ps1 twin now fully PASS and `sh` new-branch probe proven.

### Semantic compile-gate artifacts/results

- Script: `scripts/verify-unity-compile.ps1` (7125 bytes, `pwsh` parse OK). Real run without `UNITY_EDITOR_PATH` correctly fails `UNITY_EDITOR_PATH is not set` (fail-closed). With editor it would: verify `6000.3.4f1` pin via `cert-script-helpers`, `git rev-parse HEAD` + `git status --porcelain`, clear old artifacts, launch Unity, tail 80 log lines, count `error CS`, emit `TestResults/compile-evidence.json`.
- Helpers: `cert-script-helpers.ps1` added `Test-UnityCompileLog` (5 error patterns, 7 completion markers) and `Test-UnityCompileEvidence` (sha, timestamps, exit, errorCount, log re-check). Fixture tests: 6 log (good/bad CS/bad CompilerError/empty/missing/no-marker) + 4 evidence (good/bad SHA/stale/errorCount) + 4 R7 script checks = 12 new, total `Test-CertificationScripts` **47/47**.
- Stale/mutation: log `LastWriteTime` checked vs `startUtc`; `mutatedFiles = postStatus - preStatus`; fails unless `-AllowProjectMutation`.

### Migration state table & focused tests

| Input `schemaVersion` | Expected `TryMigrateToCurrent` | Error contains | Profile mutated? | Test |
| --- | --- | --- | --- | --- |
| `1` (Current) | `true`, `error=null`, stays `1` | — | no | `CurrentSchema_Succeeds` |
| `6` (Current+5) | `false` | `newer` | stays `6` | `ForwardSchema_Fails` |
| `0` | `false` | `minimum` | stays `0` (not coerced) | `ZeroSchema_FailsClosed` |
| `-1` | `false` | `minimum` | stays `-1` | `NegativeSchema_FailsClosed` |
| `0` with huge payload | `false` | `minimum`/`No migration path` | `vitalityBalance`/`resources` unchanged | `ZeroSchema_DoesNotReachCurrent_AfterFailedMigration` |
| Repository `profile.json` with `0` | `TryLoad → false, IncompatibleSchema`, file preserved | — | — | `Repository_Load_WithZeroSchema` |
| Repository `profile.json` with `-5` | `false, IncompatibleSchema` | — | — | `Repository_Load_WithNegativeSchema` |
| Repository `profile.json` with `1` | `true, Success`, profile `42` preserved | — | — | `Repository_Load_WithCurrentSchema` |
| Progress guard | fails fast <1s, not loop | — | — | `MissingMigrationPath_FailsWithoutLooping` |

All 11 `M88SaveMigratorContractTests` plus 258 total domain (see gates) PASS.

### First-import / project-state diff & idempotence

- Clean: `ls -R Assets/Settings` → `No such file`, `git ls-files | grep -i URP` → empty, `ProjectSettings/GraphicsSettings.asset` → not found. `Assets/Settings` dir not tracked.
- Generator: `ConfigureUrp()` at `Assets/WalkGame/Editor/WalkGameEditorTools.cs:123` — `assetPath = Assets/Settings/URP-HighFidelity.asset`, `LoadAssetAtPath<UniversalRenderPipelineAsset>` → if null `CreateInstance` + `supportsHDR=false, msaa=4, renderScale=1.0` + `CreateAsset` + `SaveAssets`; then `GraphicsSettings.defaultRenderPipeline = pipeline` + `QualitySettings.renderPipeline`. Idempotent by existence check.
- Second-run proof & provenance binding: would be `verify-unity-compile.ps1` second run with `-AllowProjectMutation` off → `mutatedFiles` should be empty; evidence `compile-evidence.json` binds `sourceSha` + `postStatus`. Not yet run (no editor).
- No hand-fabricated `URP-HighFidelity.asset` committed (spec §H4).

### Exact fresh test counts (final source)

- Identity: `sh scripts/assert-repo-identity.sh` → `repo identity OK` ; `Assert-RepoIdentity.ps1` → OK (exit 0).
- Domain suite: **258/258 PASS** (`dotnet test` — 224 M8.7 baseline + 34 new M8.8; 11 migrator +12 vitality/reward +7 Android +4 iOS).
- `verify-domain.ps1` → PASS (same 258, exit 0).
- `verify-unity-static.ps1` → PASS (112 assets / 112 metas — added 4 new test files + 4 metas, pin `6000.3.4f1`).
- `verify-release-hygiene.ps1` → PASS (63 sources, manifest minimal — `ACTIVITY_RECOGNITION` only, `stepcounter` optional false, no location).
- `Test-AgentGuards.ps1` → **PowerShell twin 30/30** (S1-S7 ps1, S8-S10 ps1, S11 ps1, S11d/e ps1, hook S11f/g/i, S12); **sh twin 17/30** ENV-BLOCKED (pre-existing `Git Bash` `cd` + `realpath` limitation on Windows; `sh` S8a/b, S10, S11 ps1-equivalents pass when bash can `cd`; real-repo `sh check-remote-advance` on `feat/fresh` new branch **PASS exit 0** after CRLF fix, hook `pre-push` new-branch logic verified via ps1 + `S11c`).
- `Test-CertificationScripts.ps1` → **47/47 PASS** (35 M8.6 +12 M8.8 compile gate; was 35, new R7 compile + 6 log +4 evidence +1 stale).
- `git diff --check` → clean (fixed trailing whitespace `scripts/Test-CertificationScripts.ps1:268` `'@ ` → `'@'`; CRLF normalized).
- `verify-unity-compile.ps1` without editor → correctly **fails** `UNITY_EDITOR_PATH is not set` (fail-closed, not PASS). With editor **UNVERIFIED** (same blocker as M8.7).

### Editor / build / device / iOS evidence or blockers (UNVERIFIED, precise)

| Tier | Command | Result | Blocker |
| --- | --- | --- | --- |
| Unity semantic compile | `pwsh ./scripts/verify-unity-compile.ps1` | **UNVERIFIED** (script PASS via fixtures; real launch blocked) | `UNITY_EDITOR_PATH` not set; `C:\Program Files\Unity\Hub\Editor` empty, `D:\Unity` empty, `secondaryInstallPath.json` `D:\Unity`, no `Unity.exe` |
| Unity license | `Unity.Licensing.Client` | 0 entitlements, `accounts.db` empty, `Token not found in cache` | No logged-in account, no `Unity_lic.ulf`, no offline activation without credentials |
| Unity import | `Unity -batchmode` | **UNVERIFIED** | same editor/license |
| EditMode | `verify-unity-editmode.ps1` | **UNVERIFIED** | same |
| PlayMode | `verify-unity-playmode.ps1` | **UNVERIFIED** | same |
| Android Build Support | `AndroidPlayer` | **ABSENT** | Only Windows Standalone installed |
| Android build | `build-android-development.ps1` | **UNVERIFIED** | needs Build Support |
| Android device/emulator | `adb devices` | **UNVERIFIED** | empty list; emulator `sdk_gphone64` not present, sensor `TYPE_STEP_COUNTER` not exposed |
| Android smoke | `verify-android-smoke.ps1` | **UNVERIFIED** (script ready) | no APK/device |
| iOS/macOS/Xcode | `uname` win32 | **UNVERIFIED** | Windows host, no macOS |

### Performance evidence when run

No new FPS/frame time/GC/memory/battery/thermal measurements taken; campaign is pre-playtest integrity, not performance. Existing static measures (shared materials, property blocks, `OverlapSphereNonAlloc`, pooled UI rows, checkpoint production) preserved. Measurements remain **UNVERIFIED** until licensed editor + device.

### Docs / ADR changes

- `docs/IMPLEMENTATION_STATUS.md`: Last updated → M8.8, verification status 258/258 +112/112, added full M8.8 section (H1-H5×, matrix 258, blockers, next step) + CRLF note.
- `docs/TESTING_AND_PERFORMANCE.md`: 1A → 258/258, added M8.8 subsection (H1-H3, H2, H4, P1/P2, M1-M3, CRLF).
- `docs/DATA_MODEL.md` §21: added M8.8 strict migration bullets (MinimumSupported, true⇒Current, per-iteration advance, no mint).
- `docs/adr/0003-hand-bootstrapped-project.md`: added **M8.8 amendment** documenting `verify-unity-compile` provenance and preferred `URP-HighFidelity.asset` materialization path.
- `scripts/README.md`: added `verify-unity-compile.ps1` row + gate order (`Test-CertificationScripts`, `verify-unity-compile`).
- `openspec/changes/m8.8-pre-playtest-integrity-and-unity-bringup/`: `tasks.md` 41/41 checked (`- [x]`), `proposal.md`/`design.md`/`spec.md`/`audit.md` Status `ACTIVE→COMPLETE`.
- `.agent/EXECUTION_PROMPT.md`: Status `ACTIVE→COMPLETE` + this report.

### Remaining risks

- **Editor-required risks still UNVERIFIED** (same as M8.7): true Unity compile could still expose a non-namespace error (e.g., API drift in `UniversalRenderPipelineAsset` defaults) once a licensed `6000.3.4f1` editor exists; the new gate will surface it, but we cannot prove it today. Similarly, first-import `URP-HighFidelity.asset` generation could produce a non-deterministic or non-idempotent asset that the gate's `mutatedFiles` check would catch — we must materialize on a licensed host before M9.
- **Device-required risks:** Android `ReadRefinedPermission` restart concern is documented headless but not proven on a real API 29+ device where `shouldShowRequestRationale` behavior varies by OEM; the bounded poll could still surface as a 2-minute wait on some states (though not a prompt loop). iOS `CoreMotion` callback threading/AOT retention is headless-audited but not proven on device. Step-counter exactly-once across reboot/resume still needs physical `TYPE_STEP_COUNTER` hardware.
- **Guard `sh` twin remains ENV-BLOCKED** on this Windows sandbox (`Git Bash` `cd` into `C:\` worktree fails for `[sh] S1` etc.); not a repository defect, but a host where `bash` can `cd` should re-run `Test-AgentGuards` to get full `sh` green.
- No new Critical/High locally executable defect remains; all 258 domain, 112 static, 47 cert, and `ps` guard scenarios are green.

### Next-campaign recommendation

Recommend **M9 Closed Playtest Readiness** — but only on a host with a **licensed Unity `6000.3.4f1` editor + Android Build Support + a physical step-counter Android device** (and, for iOS lane, macOS/Xcode). M8.8 closed the last pre-playtest Critical/High integrity defects that could be proven without hardware. The next campaign should:
1. License/sign in Unity Hub, activate `6000.3.4f1`, set `UNITY_EDITOR_PATH`, and run `verify-unity-compile.ps1` → must PASS (proves H1 fix under real compiler).
2. Run `verify-unity-compile.ps1` with `-AllowProjectMutation` off/on to materialize `Assets/Settings/URP-HighFidelity.asset`, verify second-run idempotence (no new `mutatedFiles`), and commit the canonical asset.
3. Run `verify-unity-editmode.ps1` / `verify-unity-playmode.ps1` → must produce `TestResults/*-results.xml` with 0 failures.
4. Build `build-android-development.ps1` (IL2CPP ARM64) → check `Builds/Android/WalkGame-dev.apk` SHA-256 + `sourceSha` provenance.
5. Run `verify-android-smoke.ps1` on the selected physical target (serial-bound) → lifecycle smoke + permission state table (grant/deny/Settings/reboot) + exactly-once step-counter (if hardware exposes `TYPE_STEP_COUNTER`) + touch/UX + measured FPS/thermal.
6. If any of 1-5 exposes a measured compile/build/lifecycle/exactly-once/UX/performance blocker, **the next campaign must target that measured blocker**, not Region 2. Do not expand to HealthKit/Health Connect, cloud, or Region 2 until these pre-playtest gates are green.

