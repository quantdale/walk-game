# M8.8 Spec — Pre-Playtest Integrity & Unity Bring-Up Closure

Status: ACTIVE
Normative language: MUST / MUST NOT / SHOULD

## S1 — Repository truth and reconciliation

The executor MUST prove the checkout is `quantdale/walk-game` before mutation.

The executor MUST fetch current origin/main and compare it with planned-from `cf260d04fefbb2d5e7da265de5ae03a9aa768a0a`.

If main advanced, the executor MUST inspect and reconcile intervening changes before implementing M8.8. It MUST NOT blindly overwrite equivalent newer fixes.

The executor MUST NOT import files or state from `quantdale/simple-walk-game`.

## S2 — Editor source compiles semantically

`Assets/WalkGame/Editor/WalkGameEditorTools.cs` MUST reference Unity API types through valid namespaces/qualification.

When pinned Unity is available, the Editor assembly MUST compile with zero compiler errors before EditMode, PlayMode or Android build may be called PASS.

A static text/asset audit MUST NOT be used as semantic compile evidence.

## S3 — Dedicated semantic compile/import gate

The repository MUST contain an explicit semantic Unity import/compile verification command separate from `verify-unity-static.ps1`.

The command MUST:
- require exact Unity `6000.3.4f1`;
- produce fresh current-run log/evidence;
- bind evidence to source SHA and dirty state;
- return non-zero on editor launch failure, compiler error or import failure;
- reject stale/missing evidence;
- preserve failure logs;
- expose unexpected project mutation.

If Unity is unavailable, wrapper semantics MUST still be fixture/parse tested where practical, while actual EDITOR-COMPILE remains UNVERIFIED.

## S4 — Compile gate cannot false-green H1

The campaign MUST add a regression or certification fixture proving that a compiler-error log/current-run failure cannot be reported as PASS merely because Unity process exit semantics are ambiguous.

The exact H1 namespace defect MUST be fixed in source, not allowlisted.

## S5 — Save migration success postcondition

For every profile:
if `SaveMigrator.TryMigrateToCurrent(profile, out error)` returns true, then `profile.schemaVersion` MUST equal `SaveSchemaVersions.Current`.

A lower version MUST NOT be silently accepted unchanged.

## S6 — Unsupported pre-v1 schemas fail closed

Because v1 is the repository's initial defined save schema, an explicit version below the minimum supported schema MUST fail unless M8.8 introduces a real, documented deterministic migration for it.

Zero and negative schema tests MUST exist.

The implementation MUST NOT coerce an unknown lower schema to v1 merely by assigning the version field.

## S7 — Migration always advances or fails

Each migration-loop iteration MUST either:
- advance exactly to the expected next schema version; or
- return failure.

A missing migration case, unchanged version, backward version or version jump MUST fail deterministically rather than break-and-succeed or loop forever.

Tests MUST cover current, lower unsupported, newer unsupported and progress-guard behavior.

## S8 — Save/load policy remains fail closed

M8.8 MUST preserve:
- forward-schema refusal;
- trusted backup/quarantine behavior;
- in-place rollback contract;
- M8.7 structural repairs;
- exactly-once movement dedup/cursor state.

A migration failure MUST NOT cause silent new-profile creation over existing save material.

## S9 — Clean-checkout project-state provenance

The executor MUST inspect first-import/project-setup mutation with genuine pinned Unity when available.

Stable canonical Unity-generated settings required to reproduce the certified project SHOULD be tracked after editor generation.

If generation remains intentional, certification MUST record and verify the generated-state provenance/idempotence.

The executor MUST NOT hand-fabricate opaque Unity serialized assets without the editor to satisfy this requirement.

If Unity is unavailable, this requirement remains UNVERIFIED with the exact blocker; source-only work may still complete.

## S10 — Project setup is idempotent

With a real editor, running project setup twice from the materialized canonical state MUST NOT produce unexplained additional tracked changes or semantic configuration drift.

Unexpected second-run mutation MUST be investigated before editor/build certification is green.

## S11 — Android denial/restart semantics are explicitly tested

The Android motion permission path MUST have a documented/tested state table covering fresh, granted, denied, post-restart, Settings change and unavailable states to the extent the platform exposes them.

The executor MUST reproduce the process-restart denial concern before making a platform behavior claim.

The request path MUST remain bounded and MUST NOT stack prompts.

A source/headless test MUST NOT be labeled physical-device proof.

## S12 — iOS callback/provider lifetime is ownership-safe

The iOS provider/native bridge MUST preserve ADR 0011 ownership across:
- pending historical query;
- provider shutdown;
- GameHost recomposition;
- late callback;
- live session stop.

The executor MUST verify managed callback delegate lifetime is safe for IL2CPP/AOT before claiming iOS readiness.

If macOS/Xcode/device are absent, the platform tier MUST remain UNVERIFIED; only deterministic source-level invariants may be marked PASS.

## S13 — Vitality spend requires an audit reason

Every successful Vitality spend MUST have a non-empty reason code.

An invalid spend reason MUST fail before changing balance or appending history.

Existing valid spend behavior MUST remain unchanged.

## S14 — Canonical reward arithmetic does not wrap

Resource grants and region score rewards MUST NOT use unchecked overflow behavior that can wrap canonical progress.

Boundary tests MUST prove positive overflow and negative underflow cannot silently corrupt state.

Normal authored reward amounts MUST retain their existing result.

## S15 — Presentation shader failure is non-fatal or explicitly certified

Procedural presentation code SHOULD avoid constructing materials from a null shader reference.

The executor MUST disposition identified `Shader.Find` fallback paths during semantic/build/device sweep. A change is required if a null path is reproducible or trivially guardable without changing intended rendering.

## S16 — Fresh baseline and final regression

At campaign start and after implementation, run every locally available applicable gate:
- repository identity guard;
- `dotnet test verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj`;
- `scripts/verify-domain.ps1`;
- `scripts/verify-unity-static.ps1`;
- `scripts/verify-release-hygiene.ps1`;
- `scripts/Test-AgentGuards.ps1`;
- `scripts/Test-CertificationScripts.ps1`;
- new semantic-compile wrapper fixture tests;
- focused migration/ledger/reward regressions;
- `git diff --check`.

Any new Critical/High regression MUST be fixed before completion.

## S17 — Real editor/build/device gates are distinct

When prerequisites exist, the executor MUST run in order:
1. semantic Unity compile/import;
2. EditMode;
3. PlayMode;
4. Android build when Build Support exists;
5. selected-target lifecycle smoke;
6. genuine step-counter/UX/performance where real hardware permits;
7. iOS only with genuine macOS/Xcode/signing/device.

A successful earlier tier MUST NOT imply a later tier.

## S18 — Evidence is bound to current source

Every newly green editor/build/device tier MUST identify current source SHA and relevant artifact/tool/target identity.

Stale evidence, historical counts or another branch's artifact MUST NOT be called current PASS.

## S19 — No feature expansion

M8.8 MUST NOT add Region 2, cloud/accounts/social/multiplayer, health-platform integrations, analytics rollout, broad visual redesign or unrelated economy/content work.

## S20 — 12-hour autonomous continuation

The executor SHOULD use up to 12 hours of autonomous work while legitimate in-scope work remains.

It MUST NOT stop after the first successful fix if later executable requirements remain.

It MUST NOT pad time with unrelated changes or repeatedly retry an unchanged external blocker.

If all legitimate executable scope is complete earlier, it SHOULD close the campaign honestly.

## S21 — Completion and next-campaign decision

M8.8 may be marked COMPLETE when all locally executable normative requirements are done, all available gates are fresh and green, external blockers are explicit, and no Critical/High pre-playtest defect remains.

M9 Closed Playtest Readiness SHOULD be recommended only then.

A measured real compile/build/device blocker MUST instead drive the next focused campaign.
