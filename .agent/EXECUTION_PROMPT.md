# Execution Prompt — M8.8 Minimal Blocker-Closure Sequence

Status: ACTIVE
Canonical OpenSpec: `openspec/changes/m8.8-pre-playtest-integrity-and-unity-bringup/`
Sequence baseline: `main@0c710e70e8348e4b2cb57b92bd0878283a6e6c49`
Repository: `quantdale/walk-game`

## Mission

Close M8.8 using the smallest dependency-ordered execution sequence that can produce trustworthy pre-playtest evidence. Do not expand into M9, optional polish, speculative platform rewrites, or unrelated hardening.

The confirmed defects are:

- **H1:** `WalkGameEditorTools.cs` lacks the namespaces required for `GraphicsSettings` and `IPostprocessBuildWithReport`.
- **H2:** `SaveMigrator.TryMigrateToCurrent` can return true while the schema remains below `SaveSchemaVersions.Current`.
- **H3:** no dedicated fail-closed semantic Unity import/compile gate protects the repository from H1-class false greens.
- **H4:** clean-checkout URP/project state has not been proven reproducible.

The Android denial-after-restart and iOS provider-lifetime concerns are **unverified risks**, not confirmed defects. Reproduce them before changing platform behavior.

## Non-negotiable evidence rule

Maintain one evidence ledger throughout execution. Every result must be exactly one of:

- **VERIFIED PASS:** freshly executed against the recorded source SHA, with command/scenario, environment, and artifact/log identity.
- **VERIFIED FAIL:** freshly reproduced against the recorded source SHA, with minimal reproduction and captured failure.
- **UNVERIFIED:** not executed because a named prerequisite is absent or the tier was not reached.
- **HISTORICAL:** prior evidence used only as context.

Never convert static inspection, fixture tests, historical counts, an earlier tier, or lack of reproduction into a runtime PASS. Never call an unexecuted risk a defect. A source-only test may verify a state machine but is not physical-device evidence.

## Startup — one short preflight

1. Read `AGENTS.md`, `.agent/PLANNER_HANDOFF.md`, this prompt, and the M8.8 OpenSpec.
2. Prove repository identity. Synchronize from current remote `main`; inspect commits after the sequence baseline and preserve equivalent newer work.
3. Work on local `main` as required by the active planning workflow, acquire the writer lease, and record start SHA, dirty state, Unity/editor/build-support availability, Android device/API level, and macOS/Xcode/iOS-device availability.
4. Run the currently available headless baseline once:
   - `dotnet test verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj`
   - `./scripts/verify-domain.ps1`
   - `./scripts/verify-unity-static.ps1`
   - `./scripts/verify-release-hygiene.ps1`
   - `./scripts/Test-AgentGuards.ps1`
   - `./scripts/Test-CertificationScripts.ps1`
   - `git diff --check`
5. Record fresh results; label unavailable editor/device tiers UNVERIFIED and continue. Do not spend time rerunning an unchanged external blocker.

## Execution sequence

Complete each stage before advancing. If a stage exposes a blocker that invalidates later stages, fix it and rerun only the affected gate.

### Stage 1 — Close the deterministic confirmed source defects

1. Fix H1 using the actual Unity namespaces or fully qualified API names. Do not use reflection, text-check exceptions, or suppression.
2. Fix H2 so:
   - success implies `profile.schemaVersion == SaveSchemaVersions.Current`;
   - v0, negative, and future schemas fail closed unless a real migration exists;
   - each migration step advances exactly as designed or fails;
   - unknown lower material is never relabeled as current.
3. Add focused regression tests for current, zero, negative, future, missing-step, no-progress, backward, and version-jump migration cases.
4. Run only the focused tests plus domain verification needed to prove H2. H1 remains source-fixed but semantic status stays UNVERIFIED until Stage 2.

**Exit:** H2 is freshly VERIFIED PASS; H1 is patched and awaiting semantic proof. Do not perform optional numeric, Vitality-reason, shader, UX, or performance work unless a gate in this sequence directly exposes it as a blocker.

### Stage 2 — Add semantic compile evidence, then prove clean-checkout/URP reproducibility

1. Add one dedicated semantic Unity import/compile wrapper for pinned Unity `6000.3.4f1`. It must:
   - fail on wrong editor identity, launch/import/compiler errors, or stale evidence;
   - bind evidence to source SHA and dirty state;
   - preserve a fresh full editor log and concise machine-readable result;
   - report unexpected tracked mutation;
   - have fixture tests for success, compiler failure, launch failure, wrong version, stale log/evidence, and unexpected mutation.
2. Run wrapper fixture tests. These verify wrapper semantics only; label real Unity compilation UNVERIFIED until Unity runs.
3. If the pinned licensed editor is available, use a disposable clean checkout/worktree at the recorded SHA:
   - run semantic import/compile before project setup;
   - capture the exact first-import diff;
   - run the repository project setup;
   - capture generated URP/project state;
   - run setup a second time and prove idempotence;
   - track stable editor-generated canonical state if required, or formally bind deterministic generated-state hashes/diffs into evidence;
   - never hand-author opaque Unity serialized assets;
   - rerun semantic compile from a second clean checkout of the resulting source.
4. Sweep all Unity assemblies after the first compile error; do not stop with H1 if additional semantic failures appear.

**Exit:** H1 and H3 are VERIFIED PASS only after a real fresh semantic compile. H4 is VERIFIED PASS only after clean-checkout generation/provenance and second-run idempotence are proven. Without licensed pinned Unity, H1 source fix and wrapper fixtures may pass, but H1 semantic proof and H4 remain UNVERIFIED with one precise environment blocker.

### Stage 3 — Reproduce the two platform risks; change code only on VERIFIED FAIL

#### Android denial/restart

Run on a real supported Android target when available:

1. fresh install with permission undecided;
2. deny permission;
3. force-stop/kill the process;
4. relaunch and observe refresh state before requesting again;
5. request again and verify prompt/rationale behavior is bounded;
6. change permission in Settings, relaunch, and refresh;
7. capture API level, app/source SHA, log, observed native result, refined C# result, and prompt count.

If VERIFIED FAIL, make the smallest correction preserving exactly-once activity credit and add deterministic regression coverage before rerunning the device scenario. If no suitable device/build exists, mark the physical scenario UNVERIFIED; do not infer PASS from mocks or source review.

#### iOS provider lifetime

First add or run the smallest deterministic source-level lifecycle harness covering pending history query, shutdown/recomposition, late callback, live-session stop, provider generation, and exactly-once completion. Then, only with macOS/Xcode/signing/device available, reproduce under IL2CPP/AOT and capture callback/delegate lifetime evidence.

If VERIFIED FAIL, make the smallest ownership/lifetime fix consistent with ADR 0011, extend the regression harness, and rerun. Without the Apple toolchain/device, source-level invariants may be VERIFIED PASS while runtime iOS remains UNVERIFIED.

**Exit:** each risk is either runtime VERIFIED PASS, reproduced/fixed/reverified, or explicitly UNVERIFIED. Lack of reproduction is never permission for a speculative rewrite.

### Stage 4 — Lock the regressions and certify the final source

1. Add new regression gates to CI/current verification routing:
   - migration invariant tests;
   - semantic-wrapper fixture tests;
   - real semantic compile gate where a licensed Unity runner exists;
   - deterministic Android denial/restart state-machine tests;
   - deterministic iOS callback/provider-lifetime tests.
2. Confirm every gate is fail-closed and cannot accept missing, stale, wrong-SHA, wrong-editor, or wrong-target evidence.
3. From final source, rerun the complete available baseline list from Startup plus all new focused gates.
4. If Unity is available, run semantic compile, then EditMode and PlayMode. Run Android build/device and iOS tiers only when their real prerequisites exist. Keep each tier separate.
5. Update the M8.8 OpenSpec tasks and evidence-bearing docs with the exact final ledger. Do not rewrite historical evidence as fresh.
6. Mark this prompt COMPLETE only when all locally executable requirements pass, all confirmed source blockers are closed, unavailable tiers remain honestly UNVERIFIED, and no new Critical/High blocker is known.

## Stop conditions

Stop and report the exact blocker if repository identity fails, the writer lease is held by another session, remote `main` advances incompatibly, or publication is rejected. Do not force-push, overwrite competing work, weaken a gate, fabricate evidence, or retry unchanged external prerequisites.

## Final publication

1. Recheck remote advancement and reconcile safely.
2. Commit all intended work on local `main` with a detailed multi-line session report.
3. Push normally to remote `main`; verify remote `main` contains the final commit and local `main` has no unpushed campaign commits.
4. Report final remote SHA and the evidence ledger, grouped as VERIFIED PASS, VERIFIED FAIL, UNVERIFIED, and HISTORICAL.
5. Recommend M9 only if no confirmed Critical/High blocker remains. A newly measured blocker takes priority over M9.
