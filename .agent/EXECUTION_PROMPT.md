# Execution Prompt — M8.6 Unity First-Import & Device Readiness Certification

**Status:** COMPLETE
**Planned-From:** `main@3bbdbcca11fb20a6680dbb96e808b9df2cca31f3`  
**Planner staging branch:** `agent/walk-game/m8.6-planner-20260826`  
**Canonical OpenSpec change:** `openspec/changes/m8.6-unity-device-readiness-certification/`  
**Target implementation branch:** `agent/walk-game/m8.6-<session-id>`  
**Campaign type:** Unity first-import / semantic compile / editor runtime / Android build+device certification / performance evidence  
**Target milestone:** M8 — Device Ready  
**Autonomous work budget:** up to 12 hours

## Mission

Reacquire repository truth, reconcile this planner staging branch with current `origin/main`, then execute the **entire M8.6 OpenSpec in one coherent autonomous campaign**.

M8.5 closed the known headless runtime-ownership/rollback-fidelity tranche and explicitly recommended real Unity/device certification rather than inventing another speculative hardening pass. The current repository has strong engine-free evidence but still lacks fresh semantic Unity compile, EditMode, PlayMode, Android IL2CPP/ARM64 build, genuine step-counter lifecycle, physical mobile UX, and performance/battery/thermal proof.

Do not treat this as a request to fix one compiler error and stop. Work through the prioritized certification stack for up to the full 12-hour budget while legitimate in-scope work remains:

`identity/reconcile -> baseline -> Unity import/compile -> certification harness -> EditMode -> PlayMode -> Android build -> Android smoke -> physical step-counter/UX -> performance -> conditional iOS -> final rerun/docs/push`.

Elapsed time is not a success metric. Do **not** manufacture busywork to consume 12 hours. If a lane is blocked by licensing, UAC/elevation, Build Support, physical hardware, macOS/Xcode or signing, capture the exact reproducible blocker once and move to another legitimate lane. If every executable requirement is complete earlier, finish honestly.

## Absolute repository boundary

This repository is **`quantdale/walk-game`**. It is NOT `quantdale/simple-walk-game`.

Before any mutation:

```bash
sh scripts/assert-repo-identity.sh
# or
./scripts/Assert-RepoIdentity.ps1
```

STOP on mismatch. Never import code, branch names, SHAs, prompts, status claims or assumptions from the sibling repository.

## How to consume this planner branch

This OpenSpec and ACTIVE prompt were staged on `agent/walk-game/m8.6-planner-20260826` from `main@3bbdbcca...`.

At session start:

1. fetch `origin`;
2. check out/read the planner staging branch so you have this package;
3. inspect current `origin/main` and every commit after `3bbdbcca...`;
4. reconcile the planner package with any newer main changes;
5. create a dedicated implementation branch/worktree `agent/walk-game/m8.6-<session-id>` that **contains the planner package plus current authoritative main**;
6. acquire the repository writer lease before the first implementation mutation;
7. do not implement directly on the planner staging branch.

If current main already contains an equivalent/newer M8.6 package, use the newer authoritative package and do not duplicate this staging copy.

## Required reading — in full before implementation

Canonical OpenSpec:

1. `openspec/changes/m8.6-unity-device-readiness-certification/audit.md`
2. `openspec/changes/m8.6-unity-device-readiness-certification/proposal.md`
3. `openspec/changes/m8.6-unity-device-readiness-certification/design.md`
4. `openspec/changes/m8.6-unity-device-readiness-certification/specs/device-readiness/spec.md`
5. `openspec/changes/m8.6-unity-device-readiness-certification/tasks.md`

Repository/global requirements:

- `AGENTS.md`
- `.agent/PLANNER_HANDOFF.md`
- `docs/IMPLEMENTATION_STATUS.md`
- `docs/MASTER_PLAN.md`
- `docs/ROADMAP.md`
- `docs/TECHNICAL_ARCHITECTURE.md`
- `docs/DATA_MODEL.md`
- `docs/ACTIVITY_REWARD_SYSTEM.md`
- `docs/MOBILE_ACTIVITY_INTEGRATION.md`
- `docs/PRIVACY_SAFETY_ANTI_CHEAT.md`
- `docs/TESTING_AND_PERFORMANCE.md`
- `docs/DEVICE_CERTIFICATION_CHECKLISTS.md`
- `docs/AGENT_EXECUTION_GUIDE.md`
- ADR 0007, 0008, 0009, 0010, 0011 and any newer ADR after fetch.

If this adapter conflicts with a normative M8.6 spec requirement, the OpenSpec requirement wins subject to repository-global safety/integrity rules.

## Planner audit result — seed evidence, not a substitute for your own reproduction

The planner reconciled the complete recursive tree, current M8.5 completion state, roadmap/status/docs, CI/static/test boundaries, Unity editor/build tooling, Android smoke/device checklist, native integrations and representative post-M8.5 runtime paths.

At planning time:

- `main = 3bbdbcca11fb20a6680dbb96e808b9df2cca31f3`;
- M8.5 is COMPLETE and reports historical 213/213 standalone tests;
- no open PRs or open issues compete with M8.6;
- Unity compile/EditMode/PlayMode remain UNVERIFIED;
- Android Build Support and real device step-sensor evidence remain UNVERIFIED;
- iOS/Xcode/device and physical performance remain UNVERIFIED.

Planner-confirmed/high-confidence certification gaps:

### F1 — likely Editor assembly compile blockers

`Assets/WalkGame/Editor/WalkGameEditorTools.cs` uses `GraphicsSettings` (normally `UnityEngine.Rendering`) and `IPostprocessBuildWithReport` (normally `UnityEditor.Build`) without the corresponding namespace imports/qualification.

The standalone `.NET` project intentionally does not compile `Editor`, `App`, `UI`, `World` or platform Unity assemblies, and `verify-unity-static.ps1` explicitly does not perform semantic Unity compilation. Therefore the current 213-test headless result cannot certify this file.

**Executor rule:** if licensed Unity is available, reproduce the compiler result before patching. If Unity emits the predicted errors, fix the smallest namespace/API root cause, inspect the whole Editor assembly for equivalent compile drift, and rerun cleanly. Do not report this prediction as a confirmed compiler failure unless the editor reproduces it.

### F2 — EditMode verifier can overstate evidence

`verify-unity-editmode.ps1` treats Unity exit 0 as success without requiring `editmode-results.xml`, while the PlayMode runner at least requires its result artifact.

**Required end state:** EditMode and PlayMode runners fail closed on missing/invalid/incomplete results and require zero test failures.

### F3 — Android smoke target is not deterministic with multiple adb devices

The smoke script enumerates multiple targets but does not bind commands to a serial. The physical-device checklist requires exactly one target.

**Required end state:** explicit serial or fail-closed exactly-one selection; every adb call bound to the chosen serial; artifact summary records target metadata, step-counter availability, APK hash and source SHA. Emulator/no-step-counter results are labeled lifecycle-only.

### F4 — the largest remaining risk is integration evidence

The existing PlayMode suite covers the right vertical-slice invariants, but it has never produced fresh current-tree licensed Unity evidence. Treat semantic compile/import as P0 before meaningful device certification.

Treat F1-F4 as the minimum starting evidence. Re-audit every affected file/call path after pulling current code. Fix newly discovered Critical/High correctness/build/runtime defects and Medium certification-integrity defects required for truthful evidence. Do not expand into unrelated features.

## Startup sequence — mandatory

1. Prove repository identity.
2. Fetch remote state and reconcile planner staging branch against current main.
3. Record start SHA, branch/upstream/worktree, recent commits, open PRs/issues.
4. Create the dedicated implementation worktree/branch containing current main + M8.6 planner package.
5. Acquire writer lease.
6. Record full environment inventory: Unity/Hub/license/modules, .NET, JDK, SDK, NDK, adb targets, physical sensor capability, Xcode/iOS availability.
7. Run every locally available baseline gate and record **fresh** exact results.
8. Read the full OpenSpec package and repository docs.
9. Start with the highest available gate; do not skip compile to chase device evidence.

## Implementation / certification directive

Execute **every locally executable checkbox and normative requirement** in the M8.6 OpenSpec. The task matrix is authoritative. Key outcome requirements are summarized below.

### A. Real Unity semantic compile

With a legitimate licensed Unity `6000.3.4f1` session:

- import/setup the project using repository-supported tooling;
- capture full import/compiler log;
- fix all compiler/asmdef/package/import blockers;
- specifically reproduce/disposition the predicted Editor namespace references;
- inspect Unity-only App/UI/World/platform assemblies invisible to the standalone harness;
- obtain a clean repeat compile/reopen confirmation.

If licensing is unavailable, document the exact blocker; never bypass activation. Continue with legitimate non-editor certification-harness work.

### B. Fail-closed EditMode/PlayMode evidence

Harden scripts so exit code alone cannot create false PASS. Require real, parseable completed result XML with zero failures and log artifacts. Then, when licensed:

- run EditMode;
- run PlayMode `RuntimeCertificationTests`;
- fix every Critical/High runtime defect;
- add focused Unity-level regression coverage;
- rerun the full editor gates.

### C. Android build readiness

If Android Build Support is installed/legitimately installable:

- verify Unity module + SDK/NDK/JDK;
- build the existing release-shaped development APK with IL2CPP + ARM64;
- preserve package/minSdk/targetSdk contract unless a documented toolchain fix requires a deliberate change;
- capture build log, APK SHA-256, size, source SHA and configuration;
- triage/fix Gradle/IL2CPP/manifest/JNI/build blockers.

If installation needs user elevation that this session cannot obtain, capture blocker and move on. No security bypass.

### D. Deterministic Android smoke

Make target selection exact. Then certify clean install, cold launch, Bootstrap stability, background/resume, supported rotation/aspect attempt, force-stop/relaunch and fatal/ANR sweep. Preserve logs/summary on failures where possible.

### E. Genuine physical step-counter lifecycle / exactly-once

Only a real device reporting `android.hardware.sensor.stepcounter` can satisfy this lane. Execute the repository device checklist, prioritizing permission, baseline, known walk, background, process death, reboot/counter reset, Expedition, location-denied fallback and duplicate-credit probes.

Any reproducible duplicate credit or repository-caused permanent movement loss is release-blocking. Preserve evidence, fix root cause, extend the required headless regressions, rebuild and rerun affected device cases.

### F. Mobile vertical-slice UX

On physical Android, audit touch/safe-area/project/placement/Explore/onboarding/permission/Expedition/audio/save-recovery surfaces. Fix blocker/high defects that invalidate M8. Defer aesthetic-only polish.

### G. Measured performance/battery/thermal

Capture Builder and Explore separately. Measure before optimizing. Record FPS/frame time, GC/memory where feasible, transition hitch, battery delta and thermal evidence. Any performance change must include before/after evidence on the same device/scenario.

### H. Conditional iOS

Run iOS only with genuine macOS/Xcode/signing/device preconditions. Otherwise record the blocker and leave the tier UNVERIFIED. Do not simulate a PASS.

## Scope exclusions

Do NOT implement:

- Region 2;
- HealthKit / Health Connect;
- new active GPS/location scope;
- cloud/accounts/social/multiplayer;
- broad art overhaul;
- Addressables migration;
- speculative performance optimization before measurement;
- unrelated economy/reward rebalance;
- licensing/elevation/signing bypasses.

If you discover a low/medium unrelated idea, record it for later rather than derailing M8.6.

## Mandatory final gate matrix

Run every available gate from the final source state and record exact command/result/artifact:

```text
repository identity guard
dotnet test verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj
scripts/verify-domain.ps1
scripts/verify-unity-static.ps1
scripts/verify-release-hygiene.ps1
scripts/Test-AgentGuards.ps1
git diff --check
Unity import/compile            (licensed editor only)
scripts/verify-unity-editmode.ps1 (licensed editor only)
scripts/verify-unity-playmode.ps1 (licensed editor only)
scripts/build-android-development.ps1 (Android Build Support only)
scripts/verify-android-smoke.ps1       (exact selected target only)
physical Android device checklist      (genuine sensor/device only)
iOS checklist                          (macOS/Xcode/signing/device only)
```

Do not label unavailable tiers PASS.

## 12-hour continuation policy

Use the detailed schedule in `tasks.md`. Operationally:

- keep working after the first compile/test/build fix while later eligible lanes remain;
- when an external blocker is unchanged, document it once thoroughly and move on;
- never idle/retry the same impossible prerequisite merely to extend wall-clock time;
- keep changes within M8.6 scope;
- if all legitimate executable work finishes before 12 hours, close honestly;
- if the 12-hour budget ends with productive work remaining, leave a precise continuation point and artifacts.

## Completion protocol

Before declaring completion:

1. rerun all affected available gates from final state;
2. repeat whole-repo searches/audits for any code path touched, including activity/persistence/provider lifecycle if those systems changed;
3. update `docs/IMPLEMENTATION_STATUS.md` with exact evidence tiers and artifact identities;
4. update testing/device docs with hardened gate semantics and measured results;
5. update OpenSpec task/status/evidence honestly;
6. change this file to `Status: COMPLETE` (or `BLOCKED` only if the entire campaign truly cannot make further legitimate progress) and append a detailed executor report containing:
   - planned/start/reconciled SHA;
   - branch/worktree/lease;
   - final SHA(s);
   - environment + editor/module/toolchain/device identities;
   - every reproduced defect/root cause/fix;
   - fresh standalone/EditMode/PlayMode counts;
   - Android build result + APK hash;
   - smoke/device case matrix and artifact paths;
   - performance/GC/memory/battery/thermal evidence;
   - iOS result/blocker;
   - docs/ADR changes;
   - remaining blockers/follow-ups;
   - next-campaign recommendation.
7. fetch/check remote advancement; reconcile deliberately if needed;
8. commit with a detailed full-session report and push the implementation branch per repository policy. Never force-push.

## Next campaign decision

If M8.6 produces materially green Unity + Android device readiness, recommend **M9 Closed Playtest Readiness / Validation**. If real measurements expose a material exactly-once or performance blocker, recommend a focused campaign on that measured blocker. Do not jump to Region 2 simply because this campaign ends.

---

## 2026-08-27 deep re-audit delta — MUST READ BEFORE EXECUTION

A second exhaustive planning audit was completed against **\`main@068e215388e5031438fc5acb6efb73e9d847f4e7\`** after the original M8.6 package was staged.

It reconfirmed that **M8.6 is the correct next campaign** and found no new headlessly provable Critical/High domain-state tranche that should replace it. It also audited the complete 288-file tracked tree, including all 107 Unity metas, all C# and native/runtime surfaces, tests, scenes, asmdefs, scripts, docs/ADRs, hooks, configs and OpenSpec state.

The detailed delta is appended to:

- \`openspec/changes/m8.6-unity-device-readiness-certification/audit.md\`;
- \`openspec/changes/m8.6-unity-device-readiness-certification/specs/device-readiness/spec.md\` (new normative E1-E10 requirements);
- \`openspec/changes/m8.6-unity-device-readiness-certification/tasks.md\` (new section 17).

Those additions are **mandatory**, not suggestions.

### Additional seed findings you must disposition

In addition to F1-F4 already listed above:

1. **Both Unity test wrappers need semantic XML validation.** EditMode can pass with no XML; PlayMode only checks existence. Missing/malformed/empty/stale/incomplete/failed result data must fail closed.
2. **Prove the Unity executable is exactly 6000.3.4f1.** \`UNITY_EDITOR_PATH\` currently provides a path, not toolchain identity.
3. **Create an explicit semantic compile/import gate.** \`verify-unity-static.ps1\` is intentionally not a compiler and must never be used as compile evidence.
4. **Bind every adb call to one serial.** This includes direct \`pidof\` and \`logcat\` calls, not only the helper.
5. **Fix clean-install uninstall semantics.** The current script comments that an absent package is acceptable but its helper throws on the non-zero uninstall result.
6. **Persist final/failure evidence truthfully.** The current smoke summary is written before optional cleanup is recorded; make the final artifact reflect final disposition.
7. **Emit provenance manifests.** Bind Unity/build/device evidence to source SHA, exact toolchain, APK SHA-256 and exact target identity.
8. **Do not equate process-alive with gameplay-ready.** Lifecycle process evidence, PlayMode composition evidence, physical UX evidence and real sensor evidence are separate tiers.
9. **Conditional iOS:** only if the real lane exists, make post-build permission-string evidence fail closed and repair the stale helper name in \`IOS_BUILD_REQUIREMENTS.md\`.

### Execution priority adjustment

Within the existing 12-hour budget, front-load **evidence-harness integrity** immediately after baseline/environment inventory, because all later PASS claims depend on it:

\`identity/reconcile -> fresh headless baseline -> toolchain/provenance preflight -> semantic Unity compile -> fail-closed test XML -> EditMode -> PlayMode -> Android build manifest -> exact-target smoke -> physical sensor/UX -> measured performance -> conditional iOS -> final rerun/docs/push\`.

Do not burn time merely to reach 12 wall-clock hours. The instruction is to permit and organize a long autonomous campaign, not to fabricate work. Continue while legitimate M8.6 work remains; finish early if every executable requirement is genuinely complete, or leave an exact continuation point if the budget ends first.

---

## Executor Report — M8.6 COMPLETE (in-repo lanes; editor/device lanes UNVERIFIED)

**Campaign branch:** `agent/walk-game/m8.6-exec-20260826`
**Start SHA (prior exec):** `d48c692ccc745947357fd97850f52fa5f2511215`
**Reconciled onto:** `e78ba78f24e77e7566b9ed3259878f6af83d24b5` (`origin/main`, the 288-file deep re-audit hardening that added mandatory R1-R9 / E1-E10 findings).
**Final SHA:** see commit `feat(cert): M8.6 re-audit hardening — R4/R6/R7/R17.2.10 fail-closed evidence` (this session).
**Lease:** `sess-20260826T234929Z-1745-771426137` on Windows host, acquired before first mutation.

### Environment inventory (this session)
- OS/shell: Windows 11 / PowerShell 7.
- .NET SDK: 9.0.300.
- Unity Hub: present; **Unity `6000.3.4f1` editor NOT installed** (`C:\Program Files\Unity\Hub\Editor` absent).
- Unity license/entitlement: **ABSENT** (no `Unity_lic.ulf`; licensing client shows no valid entitlement).
- Android Build Support (`AndroidPlayer`): **ABSENT** (editor not installed).
- JDK 17.0.20; Android SDK/NDK at `ANDROID_HOME`/`ANDROID_SDK_ROOT`; `adb` on PATH.
- Physical Android device: **NONE connected** (`adb devices` empty).
- macOS/Xcode/signing: **ABSENT**.

### Continuation work — re-audit mandatory findings (locally executable)

The branch was rebased onto `e78ba78`; the re-audit's R1-R9 / E1-E10 findings were dispositioned. The
editor/device-only findings remain UNVERIFIED (no licensed editor / Build Support / physical device);
the script-level, engine-free findings were fixed and locked by new regression tests:

1. **R4 toolchain identity preflight** — new `Get-UnityPinnedVersion` / `Test-UnityEditorMatchesPin` in
   `cert-script-helpers.ps1`; wired into `verify-unity-editmode.ps1` and `verify-unity-playmode.ps1` so a
   wrong/unpinned editor fails closed before any launch (runtime enforcement still UNVERIFIED: no editor).
2. **R5 every adb call serial-bound** — audited and confirmed all direct calls (`pidof`, `logcat`, `am`,
   `pm`, `settings`, `input`) route through the serial-bound `Invoke-Adb`. Required by re-audit; already present.
3. **R6 idempotent clean-install uninstall** — new `Uninstall-AndroidPackageIdempotent` (absent package =
   clean success; still-installed removal failure = real failure). Used for both pre-install and final
   cleanup in `verify-android-smoke.ps1`.
4. **R7 try/finally summary discipline** — `verify-android-smoke.ps1` restructured so the summary JSON and
   logcat are written in a `finally` block after the optional uninstall, recording `finalDisposition`.
5. **R17.2.10 foreground/resumed launch evidence** — new `Get-AndroidForegroundActivity`; smoke now fails
   if the expected package is not the foreground/resumed activity, not merely process-alive.
6. **F2/T2/F3/F3.4 (prior session)** — EditMode/PlayMode fail-closed XML validation, serial-bound smoke,
   and the engine-free `Test-CertificationScripts.ps1` suite (extended 16 -> **35/35 PASS** this session).

The planner-predicted `WalkGameEditorTools.cs` edit-time namespace references (F1/R1) could not be
reproduced or fixed: doing so requires a licensed Unity editor, so they remain **predicted findings**,
honestly uncertified (not claimed fixed).

### Fresh gate evidence (this session, final source state)

- Repository identity: `scripts/assert-repo-identity.sh` (and `Assert-RepoIdentity.ps1`) exit 0.
- Standalone suite: **213/213 PASS** (`dotnet test verification/WalkGame.Domain.Tests/...`).
- `verify-domain.ps1`: PASS (same suite + restore check).
- `verify-unity-static.ps1`: PASS (107 assets / 107 metas, Unity 6000.3.4f1 pin).
- `verify-release-hygiene.ps1`: PASS (63 runtime sources, manifest minimal).
- `Test-AgentGuards.ps1`: **36/36 PASS** (ps + sh + hook tiers).
- `scripts/Test-CertificationScripts.ps1`: **35/35 PASS** (R4/R6/R7/R17.2.10 + parse-only checks).
- `git diff --check`: clean (CRLF normalization only).
- Unity import/compile, EditMode run, PlayMode run, Android build, lifecycle smoke run, physical step
  sensor, UX, performance, iOS: **NOT EXECUTED — UNVERIFIED** (no licensed editor / Build Support /
  physical device / macOS). No false-green conditions remain in any certification wrapper.

### Documentation / OpenSpec changes
- `openspec/changes/m8.6-unity-device-readiness-certification/tasks.md`: section 17 re-audit checklist
  marked with DONE/UNVERIFIED dispositions; new `### 17.7 R1-R9 disposition` table; executor-evidence
  footer updated to final continuation state (35/35 cert tests, rebase onto `e78ba78`).
- `.agent/EXECUTION_PROMPT.md`: this report, updated to final continuation state.
- `docs/IMPLEMENTATION_STATUS.md`: M8.6 campaign section updated with engine-free evidence tiers.
- `docs/TESTING_AND_PERFORMANCE.md`: §1A records M8.6 harness hardening + 35/35 cert-script tests.
- `docs/DEVICE_CERTIFICATION_CHECKLISTS.md`: preconditions/recording extended with serial / APK SHA-256 /
  source SHA / lifecycle-only / finalDisposition fields.
- Hardened scripts: `cert-script-helpers.ps1`, `verify-android-smoke.ps1`, `verify-unity-editmode.ps1`,
  `verify-unity-playmode.ps1`; new regression suite `Test-CertificationScripts.ps1` (35/35).
- No ADR required: this is evidence-policy/script-hardening work, not a material architecture change.

### Remaining blockers / follow-ups
- All EDITOR/DEVICE/iOS gates blocked solely by missing environment (licensed editor, Build Support,
  physical device, macOS). No repository defect is open. R4/R6/R7/R17.2.10 harness code is in place and
  engine-free tested, but its runtime enforcement is UNVERIFIED pending the same environment.
- **Push status (BLOCKED by fail-closed guard, not force-pushed):** the implementation branch
  `agent/walk-game/m8.6-exec-20260826` has no remote counterpart (only
  `agent/walk-game/m8.6-planner-20260826` exists on origin). The pre-push race guard
  (`.githooks/pre-push`, policy layer 1) runs `git fetch origin refs/heads/<branch>` to prove there are
  no unreachable remote commits; for a never-pushed branch that fetch fails with
  `fatal: couldn't find remote ref`, and the guard refuses the push rather than assume safety. Per
  AGENTS.md ("Never force-push", "Preserve stricter local rules") the branch was **not** force-pushed and
  the hook was **not** bypassed. Recommended resolution: a human creates the remote branch (e.g. push
  from a trusted session, or the hook is adjusted to treat an absent remote ref as a safe first-push),
  after which this committed work (`0d5b188`) flows without rebase.
- Recommended next campaign: **M8 Device Ready / M9 Closed Playtest Readiness** on a host with a
  licensed Unity 6000.3.4f1 editor and, ideally, a physical step-counter Android device. A measured
  exactly-once or performance defect surfaced only under real hardware should drive a focused follow-up.
