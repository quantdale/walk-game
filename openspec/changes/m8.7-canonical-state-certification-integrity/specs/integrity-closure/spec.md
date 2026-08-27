# M8.7 Spec — Canonical State & Certification Integrity Closure

Status: ACTIVE
Normative language: MUST / MUST NOT / SHOULD

## S1 — Repository identity and reconciliation

The executor MUST prove the checkout is quantdale/walk-game before mutation.
The executor MUST fetch current origin/main and inspect changes after e78ba78.
The executor MUST treat d0c8687 as prior-session input only when that commit is actually available and its diff is inspected.
The executor MUST NOT import state from quantdale/simple-walk-game.

Scenario: stranded M8.6 commit exists locally
Given d0c8687 exists locally and the remote executor branch is an ancestor of it, the executor may push it normally after guards pass. It MUST NOT force-push.

Scenario: stranded commit is unavailable
The executor MUST not claim its fixes landed. It MUST compare current source to M8.6 requirements and implement only missing equivalents.

## S2 — Current region always resolves

After SaveValidator accepts/repairs a profile, WorldState.currentRegionId MUST resolve to a non-null RegionState.

Scenario: explicit null current-region entry
Given a current-schema JSON save whose regionStates contains the currentRegionId key with value null, loading/repair MUST NOT allow a later GetOrCreateRegionState call to return null. Boot-equivalent access MUST not throw.

## S3 — GetOrCreate self-heals null values

WorldState.GetOrCreateRegionState MUST return a non-null RegionState for every non-null/non-empty normalized key, even when the dictionary already contains that key with a null value.

The method MUST NOT silently create gameplay progression beyond the empty structural state required for the region object.

## S4 — Region storage identity is coherent

After repair, each surviving regionStates entry MUST have a non-null RegionState whose regionId matches the dictionary key or the load MUST fail closed with an explicit reason.

Scenario: key/value mismatch
Given key region.ashfall containing a RegionState whose regionId is region.other, repair MUST deterministically normalize or reject it according to the documented M8.7 policy. Downstream catalog/event identity MUST not remain split.

## S5 — Null transaction history cannot break rollback

After repair, recentVitalityTransactions MUST contain no null elements.

Scenario: failed commit after loading null transaction
Given a valid current-schema save containing a null element in recentVitalityTransactions, after load and a simulated persistence write failure, PersistenceCoordinator MUST converge without an unhandled exception. It MUST preserve the last-known-good canonical balance and non-null valid transaction entries.

## S6 — Structural invariant matrix

Tests MUST cover every persisted model family and classify nullable/container edges as:
- required and repaired;
- legitimately nullable;
- pruned when structurally unrecoverable;
- or rejected/fail-closed.

The implementation MUST NOT add broad reflection-based runtime copying that is unsafe under IL2CPP merely to satisfy tests.

## S7 — Repair cannot mint progression

Save repair MUST NOT:
- credit Vitality;
- complete restoration projects;
- restore buildings;
- grant resources;
- advance restoration stages;
- add dedup credit keys;
- mark achievements reached;
- discover lore or arrive NPCs.

Repairs may restore empty structural containers and identity fields required to make the graph usable.

## S8 — Repaired state round-trips

A repaired profile MUST serialize and deserialize into the same canonical structural shape under the current serializer/migrator. Re-running repair SHOULD be idempotent.

## S9 — Rollback remains in-place and exact

M8.7 MUST preserve the M8.5 requirement that surviving live graph objects are reused during rollback where references are expected to remain stable. Repairing malformed source state MUST NOT reintroduce stale nested keys or duplicate dedup membership.

## S10 — First push is supported only when absence is proven

A new branch MUST be normally pushable when the exact remote ref is positively proven absent.

A generic network/auth/fetch failure MUST NOT be interpreted as branch absence.

Scenario: absent ref
Local bare origin is reachable and exact branch ref does not exist. Guard returns success for a non-delete first push.

Scenario: unreachable origin
Origin cannot be queried. Guard fails closed.

Scenario: remote advanced
Exact remote branch exists with commits not contained by local tip. Guard refuses.

Scenario: force-shaped divergence
Remote tip is not an ancestor of local tip. Guard refuses; no automated force path exists.

## S11 — Guard parity

.githooks/pre-push, scripts/check-remote-advance.sh and scripts/Check-RemoteAdvance.ps1 MUST implement equivalent ref-existence/lost-update semantics. Test-AgentGuards MUST regression-test the first-push case and all safety-negative cases using local fixtures.

## S12 — M8.6 evidence integrity remains binding

Any M8.6 R1–R9 / E1–E10 requirement not already satisfied by reconciled source remains mandatory when its lane is locally executable.

At minimum, the executor MUST inspect for:
- exact Unity 6000.3.4f1 preflight;
- semantic compile/import verifier;
- fail-closed EditMode/PlayMode result validation;
- exact serial-bound adb;
- idempotent uninstall;
- final failure evidence;
- build/smoke provenance manifests;
- foreground/resumed launch evidence;
- certification-script regression fixtures.

## S13 — Evidence tier truthfulness

No editor/device/build/performance tier may be marked PASS without current-run artifacts and real prerequisites. External blockers MUST be recorded as UNVERIFIED with a specific reason.

## S14 — Full regression gate

Before completion, the executor MUST run every locally available final gate from final source:
- repository identity;
- dotnet domain suite;
- verify-domain;
- verify-unity-static;
- verify-release-hygiene;
- Test-AgentGuards;
- Test-CertificationScripts when present;
- git diff --check;
- and any genuine Unity/build/device tier available.

Any new Critical/High regression MUST be fixed before campaign completion.

## S15 — No feature expansion

M8.7 MUST NOT add Region 2, cloud, social, multiplayer, analytics rollout, health-platform integrations or unrelated content. M9 remains gated on integrity closure.
