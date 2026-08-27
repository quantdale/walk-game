# M8.7 Audit — Canonical State & Certification Integrity Closure

Status: ACTIVE planning package
Planned-From: main@e78ba78f24e77e7566b9ed3259878f6af83d24b5
Audit date: 2026-08-27
Repository: quantdale/walk-game
Target milestone: M8 — Device Ready
Autonomous execution budget: up to 12 hours

## 1. Why this re-audit exists

M8.6 completed the locally runnable evidence-integrity work in the prior executor session, but its executor commit d0c8687 did not reach the remote because the tracked pre-push hook refuses the first push of a branch whose remote ref does not yet exist. The authoritative main branch is therefore still e78ba78, not the prior executor result.

This planner pass re-established truth from current main instead of treating the prior summary as landed code. It also re-audited the canonical state graph after the M8.5 rollback work and found a new corruption-resilience defect family that is more important than moving directly to M9.

## 2. Repository truth

- Authoritative main: e78ba78f24e77e7566b9ed3259878f6af83d24b5.
- Recursive tracked tree: 288 blobs, including 87 C# sources/tests, 107 Unity meta files, 14 asmdefs, 2 scenes, Android Kotlin, iOS Objective-C++, PowerShell/shell certification tooling, docs/ADRs, hooks and OpenSpec state.
- Open PRs: none at planning time.
- Open issues: none at planning time.
- Project pin: Unity 6000.3.4f1.
- M8.5 headless baseline recorded on main: 213/213 domain tests, 107/107 Unity asset/meta static checks, 63 runtime-source release-hygiene scan.
- Licensed Unity semantic compile, EditMode, PlayMode, Android IL2CPP build, physical sensor/UX/performance and iOS remain evidence tiers that require real prerequisites.
- The prior M8.6 executor commit d0c8687 is not assumed landed.
- The missing remote branch agent/walk-game/m8.6-exec-20260826 was created at e78ba78 during this planner session. If the prior local d0c8687 commit still exists, it can now be pushed by a normal fast-forward after the repository race checks; no hook bypass or force push is needed.

## 3. Exhaustive review method

This pass used the complete recursive tree as the inventory and checked every tracked file category. All 87 C# files were fetched/scanned for unfinished stubs, direct wall-clock use, synchronous task blocking, async-void entry points, PlayerPrefs/Resources escape hatches, raw Debug logging and persistence mutation boundaries. The Android Kotlin bridge, iOS bridge, editor/build tooling, bootstrap/config, CI, hooks, all certification scripts, OpenSpec, roadmap/status/architecture and the persistence/activity/application orchestration code were reviewed directly.

The current tree contains no TODO/FIXME/HACK/NotImplementedException tranche in C# that should supersede this campaign. Direct DateTime.UtcNow use is limited to the intentional SystemClock/diagnostic logging seams. No PlayerPrefs or Resources.Load persistence bypass was found. Task result access is concentrated in already-owned completion paths rather than a newly discovered main-thread blocking pattern. The M8.3–M8.5 activity exactly-once/ownership architecture remains coherent in this static pass.

The existing e78ba78 deep re-audit already structurally verified all 107 Unity metas, GUID ownership, scene references and asmdef/config surfaces. No code has landed on main after e78ba78, so those structural results remain the correct baseline context, but the executor must still rerun fresh gates.

## 4. New findings

### H1 — Parseable save with a null RegionState can survive validation and crash boot

WorldState.GetOrCreateRegionState checks only whether a dictionary key exists. If regionStates contains the current region key with a null value, TryGetValue succeeds and GetOrCreateRegionState returns null.

SaveValidator.RepairAndValidate iterates regionStates and calls RepairRegion, but RepairRegion immediately returns for a null RegionState. It neither removes nor reconstructs the entry.

GameHost.EnsureRegionState then uses ContainsKey rather than proving the value is non-null, calls GetOrCreateRegionState, and dereferences the returned region. A JSON save can therefore be syntactically valid and schema-compatible yet crash during boot because the canonical graph is structurally invalid.

Disposition: reproduce with a deterministic save/load regression, repair or reject the invalid map entry at the load boundary, and make WorldState.GetOrCreateRegionState self-heal an existing null value as defense in depth. Preserve the canonical current-region identity and never manufacture progress.

### H2 — Region dictionary key/object identity can disagree

SaveValidator repairs a non-null RegionState but does not prove that dictionary key K equals RegionState.regionId. Downstream code alternates between dictionary lookup identity and region.regionId for catalog lookup, events and progression.

A parseable save containing regionStates["region.ashfall"] = { regionId: "region.other" } can therefore create split identity inside one canonical object graph.

Disposition: establish one deterministic invariant. For a surviving map entry, the dictionary key is authoritative storage identity and the value must be normalized to that key, unless the executor proves a safer fail-closed alternative. CurrentRegionId must resolve to a real non-null region after repair.

### H3 — Null entries in recentVitalityTransactions can defeat failed-save rollback

SaveValidator ensures recentVitalityTransactions itself is non-null but does not prune null elements.

ProfileStateCopier.CopyInto clears the target list and calls CopyTransaction for each source element. CopyTransaction dereferences the source unconditionally.

The normal commit-failure path reloads durable state through FileSaveRepository, then uses ProfileStateCopier to revert the live graph. A durable parseable save containing a null recent transaction can therefore turn an ordinary write failure into an exception inside the recovery boundary.

Disposition: add a regression that loads such material and drives a real PersistenceCoordinator failed commit. The outcome must converge to RevertedToLastKnownGood or a deliberately classified fatal state; no unhandled NullReferenceException is acceptable. Validator and copier responsibilities must be explicit.

### H4 — Structural repair coverage is not expressed as an invariant suite

The validator repairs many containers but the requirements are scattered. It currently repairs selected top-level collections, building placement, producer values and activity dedup stores; it does not have a table/test proving every serializer-visible collection/value edge is either repaired, rejected or intentionally preserved.

Disposition: build a serializer-visible structural invariant matrix covering PlayerProfile, WorldState, RegionState, BuildingState, ProducerState, ActivitySyncState, AchievementState, PlayerSettings and recent transaction history. Do not blindly sanitize legitimate forward-compatible data. Repair only structural impossibilities and values with an existing safety contract.

### H5 — First-push protection has a fail-closed deadlock, not merely a one-session accident

The tracked .githooks/pre-push performs git fetch origin <remote-ref> and refuses the push when fetch fails. A new remote branch necessarily has no ref to fetch, so the hook blocks the first push.

The standalone remote-advance scripts describe a new branch as safe, but their fetch-first implementation also treats an absent ref as an environment failure before reaching that branch of logic.

This contradicts the intended policy: new branches must be creatable without weakening lost-update protection. The prior M8.6 session was blocked by exactly this behavior.

Disposition: distinguish three states explicitly:
1. remote ref exists — fetch it and require it to be an ancestor of the local tip;
2. remote ref is proven absent — allow a normal first push;
3. remote state cannot be determined because of network/auth/transport error — fail closed.

Use deterministic local bare-remote tests. Never turn a generic fetch failure into "branch absent." Never permit force pushes or remote deletions.

## 5. Carry-forward M8.6 findings

The previous executor summary reports local fixes for exact Unity pin preflight, serial-bound adb, idempotent uninstall, final smoke evidence and foreground/resumed evidence, with Test-CertificationScripts at 35/35. Those are valuable but are not authoritative until the code is present in the reconciled implementation branch.

The M8.7 executor must:
- locate d0c8687 locally or remotely if available;
- inspect its diff before reusing it;
- carry forward equivalent M8.6 fixes without duplicating or reverting newer work;
- verify whether shared semantic Unity XML validation, semantic compile/import verification and Android build provenance are fully implemented, not merely described;
- preserve all R1–R9 / E1–E10 evidence-truthfulness requirements from M8.6 that remain relevant.

## 6. Known external evidence gaps

These are not code failures by themselves:
- licensed Unity 6000.3.4f1 semantic import/compile;
- current-tree EditMode and PlayMode;
- Android Build Support / IL2CPP ARM64 artifact;
- deterministic Android lifecycle on a selected target;
- genuine step-counter exactly-once cases;
- physical touch/safe-area/UX;
- measured FPS/GC/memory/battery/thermal;
- macOS/Xcode/signing/device iOS lane.

M8.7 may execute these if the prerequisites actually exist. Otherwise they remain UNVERIFIED with exact blockers; the session must not burn hours retrying unchanged prerequisites.

## 7. Why M8.7 outranks M9

The roadmap priority order begins with state integrity. A closed playtest should not start while a parseable local save can violate canonical graph shape and crash boot or the rollback path. M8.7 is therefore a final integrity-closure campaign: repair structural persistence invariants, make integration guards actually usable without becoming fail-open, reconcile the M8.6 work, then attempt remaining real certification tiers.

M9 Closed Playtest Readiness becomes the next recommendation only when no Critical/High state-integrity defect remains and the available M8 evidence is truthful.
