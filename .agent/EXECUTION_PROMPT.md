# Execution Prompt — M8.8 Re-Audited Pre-Playtest Completion Campaign

Status: COMPLETE
Repository: quantdale/walk-game
Re-audited baseline: main@95606565c44b7bab04e434856ad9cd65dbefd101
Executor-reconciled baseline: main@15947a222b9812cb641066f40cb8e48a276207c7
Implementation publication: main@0190c8ab59331f72e4b2ffa1636139ece6b4ab13
Canonical OpenSpec: openspec/changes/m8.8-pre-playtest-integrity-and-unity-bringup/
Autonomous objective: drive the one-region MVP as close as realistically possible to full functional, device, release, and production readiness in one long session

## Read first

Read, in order:
1. AGENTS.md
2. .agent/PLANNER_HANDOFF.md
3. this file
4. openspec/changes/m8.8-pre-playtest-integrity-and-unity-bringup/reaudit-2026-08-28.md
5. openspec/changes/m8.8-pre-playtest-integrity-and-unity-bringup/specs/pre-playtest-integrity/spec.md
6. openspec/changes/m8.8-pre-playtest-integrity-and-unity-bringup/specs/pre-playtest-integrity/revision-2026-08-28.md
7. openspec/changes/m8.8-pre-playtest-integrity-and-unity-bringup/tasks-revision-2026-08-28.md
8. the remaining M8.8 proposal/design/tasks
9. current implementation/status/testing/mobile/privacy/architecture/data docs and applicable ADRs.

The 2026-08-28 re-audit and normative revision supersede conflicting older M8.8 current-state claims and task ordering. Carry forward all non-conflicting requirements.

## Mission

Execute a combined implementation + hardening campaign. Do not start M9 and do not expand feature breadth until pre-playtest integrity is trustworthy.

At planning time, the highest-value known work is:

P1 HIGH:
- current main CI red: Test-AgentGuards 42/43, real-hook S11h fixture fails;
- Editor semantic compile namespace blocker;
- SaveMigrator false-success below Current;
- missing standalone semantic Unity compile/import gate;
- incomplete clean-checkout Unity/package/project/URP provenance;
- Android build target fixed at API 35 even though new Play submissions require API 36 starting 2026-08-31;
- no reproducible tracked iOS Xcode/build certification path for the MVP.

P2:
- Android denial classification after process restart: reproduce before changing behavior;
- iOS static callback/provider lifetime: source-test then certify under IL2CPP/AOT;
- successful Vitality spend can carry empty reason;
- canonical reward arithmetic can overflow;
- some procedural Material creation lacks final null-shader protection;
- physical performance/battery/touch/accessibility remains unverified.

Do not treat these planning-time facts as fresh executor evidence. Reproduce the relevant baseline after reconciling current main.

## Evidence discipline

Every claim must be one of:
- VERIFIED PASS: freshly executed against recorded current source with tool/environment/artifact identity;
- VERIFIED FAIL: freshly reproduced with minimum reproduction and captured evidence;
- UNVERIFIED: not executed because a named prerequisite is absent or the lane was not reached;
- HISTORICAL: earlier evidence used only for context.

STATIC does not imply EDITOR-COMPILE.
EDITOR-COMPILE does not imply EDITMODE.
EDITMODE does not imply PLAYMODE.
PLAYMODE does not imply BUILD.
BUILD does not imply DEVICE.
EMULATOR does not imply PHYSICAL STEP SENSOR.
SOURCE iOS tests do not imply IL2CPP/AOT device behavior.

## Startup

1. Prove exact repository identity. Stop on mismatch.
2. Fetch current origin/main. Inspect every commit after the re-audited baseline and preserve equivalent newer work.
3. Follow AGENTS.md one-writer/branch/worktree policy and acquire the writer lease before mutation.
4. Record start SHA, branch/worktree, dirty state and lease identity.
5. Inventory actual toolchain:
   - Unity 6000.3.4f1 editor and license;
   - Android Build Support, SDK/platform/build-tools/NDK/JDK/adb;
   - Android device/emulator, OS/API and step-counter capability;
   - macOS, Xcode, iOS SDK, signing and iOS device.
6. Run the full currently executable headless baseline once:
   - dotnet test verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj
   - scripts/verify-domain.ps1
   - scripts/verify-unity-static.ps1
   - scripts/verify-release-hygiene.ps1
   - scripts/Test-AgentGuards.ps1
   - scripts/Test-CertificationScripts.ps1
   - git diff --check
7. Record exact fresh counts/failures. Do not reuse the planning-time 224/224 or 42/43 as current proof.

## Dependency-ordered execution

### Stage 0 — Restore CI evidence integrity

Fix the guard fixture before trusting the workflow as a campaign gate.

Reproduce S11h through the real pre-push hook. Confirm whether url.*.insteadOf causes git remote get-url origin to return the local file transport and therefore makes assert-repo-identity reject the fixture before the intended branch-race logic.

Repair the fixture/test seam, not the production security invariant. Keep all test egress local.

Strengthen hook scenarios so each negative case proves its intended rejection reason/path; a generic identity failure must not satisfy a force/delete/unreachable-race test.

Run all identity, writer-lock, remote-advance, PowerShell, shell and hook cases.

Exit Stage 0 only when the guard suite is 100% green in the executor environment or an exact environment blocker is documented. Do not proceed to final certification with required CI red.

### Stage 1 — Close deterministic source/release blockers

H1 Editor:
- add actual namespace/qualification for GraphicsSettings and IPostprocessBuildWithReport;
- no reflection, suppression, or allowlist.

H2 migration:
- true must imply exact Current;
- v0, negative and unsupported lower fail closed unless a real migration exists;
- forward schema fails closed;
- every migration step advances exactly as specified or fails;
- add current/lower/negative/future/missing-step/no-progress/backward/jump tests;
- test through real save/load policy.

Android target:
- change release-shaped target from API 35 to API 36 or later;
- update assertions/docs;
- prove the generated build cannot silently target below the Play requirement;
- preserve minSdk only by deliberate supported-device policy.

Canonical hardening:
- require non-empty successful Vitality spend reason before mutation;
- eliminate unchecked canonical resource/score wraparound with explicit tested policy;
- null-guard cheap Shader.Find -> Material paths where unambiguously safe;
- keep normal authored Ashfall behavior unchanged.

Run focused tests, then full engine-free domain verification.

Exit: all deterministic engine-free changes are freshly green. H1 remains semantically UNVERIFIED until Stage 2 if Unity did not run.

### Stage 2 — Establish real Unity semantics and reproducible clean checkout

Add a dedicated fail-closed semantic compile/import verifier for exact Unity 6000.3.4f1.

It must:
- bind source SHA and dirty state;
- bind exact editor identity;
- remove/uniquely identify stale evidence;
- preserve a full fresh editor log;
- fail on launch/import/compiler errors;
- prove completion rather than only process exit;
- report unexpected tracked/untracked canonical mutation;
- emit machine-readable evidence;
- have engine-free fixtures for wrong version, launch failure, compiler error, stale/missing evidence and mutation false greens.

If genuine pinned Unity is available:
1. use a disposable clean checkout at recorded source;
2. semantic compile before setup;
3. capture first-import diff;
4. run ApplyProjectSetup;
5. capture package/project/URP changes;
6. materialize stable Unity-generated canonical state, including Packages/packages-lock.json and required ProjectSettings/URP assets where appropriate;
7. never hand-author opaque Unity serialized state;
8. run setup a second time and prove idempotence;
9. create a second clean checkout of resulting source;
10. run semantic compile again;
11. sweep every assembly after the first compiler error;
12. run EditMode and PlayMode only after compile is green.

If licensed Unity is unavailable, finish wrapper/source work and record exact UNVERIFIED blocker once. Continue to other executable lanes.

### Stage 3 — Android API 36 build, lifecycle, permission, and exactly-once certification

With Android Build Support:
- build IL2CPP ARM64 using API 36;
- record source SHA, Unity version, SDK/build tools, targetSdk, APK/AAB hash;
- verify actual generated targetSdk is 36+;
- run deterministic selected-target install/launch/background/resume/rotation/force-stop/relaunch smoke;
- prefer Android 16 coverage.

Permission denial/restart:
- fresh install undecided;
- deny;
- process death/force-stop;
- relaunch and inspect native/refined permission before request;
- request again and prove bounded/non-stacked behavior;
- Settings grant/revoke and relaunch;
- capture OS/API/source/app/log and prompt count.

Reproduce before provider rewrite. If VERIFIED FAIL, implement the narrowest fix and add a deterministic state-model regression.

Physical step target:
- require real step-counter capability;
- verify passive polling + Expedition + save/reload + process restart cannot credit the same movement twice;
- preserve staged commit/resolve ownership.

Do not call emulator lifecycle proof physical movement proof.

### Stage 4 — iOS reproducible build and lifetime certification

Implement the missing reproducible iOS path even if Apple hardware is absent:
- Unity method/entry point to generate the Xcode project deterministically;
- canonical iOS bundle/project settings;
- macOS-oriented build/cert wrapper that records source SHA, Unity, Xcode, SDK and output;
- source-level lifecycle harness for pending historical query, shutdown/recomposition, late callback, live stop and provider generation;
- explicit managed callback retention intent for IL2CPP/AOT where required.

With genuine Apple prerequisites:
- use Xcode 26 or later and iOS 26 SDK or later;
- generate Xcode project;
- verify CoreMotion and NSMotionUsageDescription;
- build/sign/install;
- exercise permission, historical query, live session, background/resume, shutdown/relaunch and late callbacks;
- preserve artifacts/logs/device identity.

No macOS/signing/device means runtime iOS remains UNVERIFIED. Do not fabricate a PASS and do not commit secrets.

### Stage 5 — User-facing hardening and measured performance

Where hardware exists, run the major vertical-slice journeys end-to-end:
- onboarding/permission-denied fallback;
- movement/Vitality;
- restoration;
- production/offline summary;
- builder placement and persistence;
- Builder -> Explore synchronization;
- Expedition start/stop/recovery;
- save corruption/recovery paths where safe in test profile.

Check:
- safe areas/orientation;
- touch target usability/readability;
- basic accessibility;
- Builder and Explore frame times;
- region transition timing;
- Expedition battery/thermal sanity.

Fix measured material defects and add regression coverage. Do not perform speculative broad optimization.

### Stage 6 — Final release certification

From final source rerun every available gate:
- repository identity;
- domain suite;
- verify-domain;
- Unity static;
- release hygiene;
- Test-AgentGuards 100%;
- Test-CertificationScripts;
- semantic-wrapper fixtures;
- all new focused regressions;
- git diff --check;
- semantic Unity compile;
- EditMode;
- PlayMode;
- Android API 36 IL2CPP ARM64 build/smoke/device;
- iOS Xcode/build/device if available.

Also verify:
- clean second-checkout reproducibility;
- package/project/URP state explainable and clean;
- no accidental secrets/debug files;
- final GitHub Actions workflow green;
- documentation matches fresh evidence exactly.

Add a regression for every material defect discovered during execution.

## Scope constraints

Do not add:
- Region 2;
- cloud/accounts/social/multiplayer;
- Health Connect/HealthKit;
- analytics/backend rollout;
- broad art/UI redesign;
- unrelated package upgrades;
- economy/content expansion;
- speculative optimization without measurement.

Do not weaken:
- exactly-once movement;
- fail-closed persistence/quarantine;
- offline-first behavior;
- passive steps without GPS;
- repository identity/writer/race guards;
- safe Git history.

## Long-session policy

Continue automatically while legitimate in-scope work remains, up to the session budget.

Start with blockers and dependency-critical work.
Validate after meaningful milestones.
Repair introduced Critical/High regressions immediately.
Do not stop after the first successful source fix.
Do not repeatedly retry an unchanged external prerequisite.
If implementation work is complete, continue into hardening.
If hardening is complete, continue into regression hunting, performance/UX review, security/privacy sanity, release certification, documentation synchronization and repository cleanup.
Stop only when additional work would be external, speculative, low-value, or outside the intended one-region MVP.

## Completion claims

M8.8 COMPLETE:
- all locally executable requirements are closed;
- no known P0/P1 blocker remains;
- current required CI is green;
- unavailable external tiers are precisely UNVERIFIED.

FULL MVP PRODUCTION-CERTIFIED is stricter:
- genuine clean-checkout Unity semantic evidence;
- Android API 36 release-shaped device evidence;
- genuine iOS Xcode 26+iOS 26 SDK build/sign/install/device evidence;
- major user journeys;
- measured performance/thermal/touch/accessibility;
- no known P0/P1 defect.

Do not conflate the two.

## Final publication

1. Update M8.8 OpenSpec/task evidence and implementation/testing docs.
2. Change this prompt to COMPLETE only when the M8.8 completion definition is actually satisfied.
3. Record start/reconciled/final SHAs, branch/worktree/lease, exact environment, every finding disposition, fresh counts, artifacts, external blockers and remaining risks.
4. Recheck remote advancement and reconcile deliberately.
5. Commit a detailed full-session report.
6. Push normally. Never force-push or delete remote refs.
7. Verify remote contains the final commit and required CI is green.
8. Recommend M9 only if no measured pre-playtest blocker remains. Otherwise the next campaign targets the measured blocker.

## Final executor report — 2026-08-28

M8.8 is COMPLETE at the locally executable implementation tier. The writer lease was
held on `main` from start SHA `15947a222b9812cb641066f40cb8e48a276207c7`; origin was
reconciled before mutation and the implementation commit
`0190c8ab59331f72e4b2ffa1636139ece6b4ab13` was pushed normally. Post-push `HEAD`,
`origin/main`, and `ls-remote` matched, and `git merge-base --is-ancestor` passed.

Fresh local gates: domain **263/263**, `verify-domain` PASS, Unity-static **112/112**,
release hygiene **63 runtime sources**, `Test-AgentGuards` **43/43**, certification
scripts **71/71**, script parse/shell syntax PASS, and `git diff --check` PASS. Required
GitHub Actions [domain-tests run #26](https://github.com/quantdale/walk-game/actions/runs/33120316547)
completed successfully for the published implementation SHA.

H0, H1, H2, H3, H5, M1, M2, and M3 received source/test or harness dispositions. Unity
semantic compile/import, generated package/project/URP state, EditMode/PlayMode,
Android IL2CPP/API 36 APK/device/step-sensor, iOS Xcode 26/iOS 26 SDK/device, and
measured UX/performance remain explicitly **UNVERIFIED** because the executor host has
no licensed Unity editor/Android Build Support or target, and is not macOS with Xcode.

This completion state is not FULL MVP PRODUCTION-CERTIFIED. M9 may proceed only on a
host that can execute the named editor/device/Apple evidence tiers; any measured blocker
found there should drive the next focused campaign.
