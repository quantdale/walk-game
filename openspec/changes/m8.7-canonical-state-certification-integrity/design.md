# M8.7 Design — Canonical State & Certification Integrity Closure

Status: ACTIVE

## 1. Design principles

### 1.1 Parseable is not valid

JSON deserialization proves syntax, not canonical graph integrity. The load boundary owns structural normalization before any service, UI, provider or rollback path receives the profile.

### 1.2 Repair the smallest impossible structure

Repair should preserve as much legitimate player state as possible. Reconstruct a missing/null container, remove a null history element, or normalize an object's storage identity when that can be done deterministically. Do not reset the whole profile for a local repairable defect.

### 1.3 Fail closed when truth cannot be reconstructed

If two surviving values conflict and the repository cannot choose an authoritative value without inventing progression, return an explicit incompatible/failure condition rather than guessing. Quarantine/destructive recovery rules from ADR 0007 remain in force.

### 1.4 Rollback is a containment boundary

ProfileStateCopier exists to recover after persistence failure. It must not introduce a second crash for state that the load validator declared usable. Validator and copier contracts must be tested together through PersistenceCoordinator, not only as isolated helpers.

### 1.5 No schema bump for pure repair

If M8.7 only canonicalizes structurally impossible values already invalid under schema v1, no schema migration is required. If the executor changes the meaning or persisted representation of a valid field, it must stop and add a real migration/ADR rather than silently reinterpret old data.

## 2. Canonical graph invariants

The executor must build an explicit invariant matrix. At minimum:

PlayerProfile:
- worldState, activityState, achievementState, settings, resources and recentVitalityTransactions are non-null after load.
- recentVitalityTransactions contains no null element after repair.
- no repair invents a Vitality credit/spend or changes the balance merely to make history look consistent.

WorldState:
- regionStates and unlockedRegionIds are non-null.
- currentRegionId is non-empty and is present in unlockedRegionIds.
- the current-region map entry resolves to a non-null RegionState after repair.
- no surviving regionStates value is null.
- each surviving RegionState.regionId is coherent with its dictionary key.

RegionState:
- every persisted set/dictionary is non-null.
- malformed building/producer entries continue to follow current prune/repair policy.
- placement is non-null and normalized.
- repair does not mark a ruin restored, complete a project, grant a resource, discover lore, arrive an NPC or advance a stage.

ActivitySyncState:
- dedup stores are non-null and Rebuild is called.
- activeSession may legitimately be null.
- repair cannot add a credited interval/session.
- existing M8.3–M8.5 exactly-once invariants remain untouched.

AchievementState / Settings:
- collections are non-null.
- existing numeric/float repairs remain deterministic.
- no achievement is awarded by repair.

## 3. Region identity policy

Preferred rule: the regionStates dictionary key is the storage identity for that entry. When a non-null RegionState under key K has an empty or conflicting regionId, normalize regionId to K during repair, provided K itself is a valid non-empty identity.

For null values:
- if the key is required because it is currentRegionId or an unlocked region, reconstruct an empty RegionState carrying exactly that key, then normal game seeding may restore only authored default structural entries as it already does;
- if a null value is for an unrelated/unreachable map key, pruning it is acceptable if that is proven less surprising and no progress can be recovered from null.
The implementation must choose and document deterministic behavior with tests.

WorldState.GetOrCreateRegionState must defend against an existing key whose value is null. A helper named "GetOrCreate" must never return null solely because the key already exists.

## 4. Transaction-history policy

A null VitalityTransaction contains no recoverable transaction data. Remove null elements during SaveValidator repair and report the repair count.

For non-null transactions, preserve fields unless an existing documented invariant requires normalization. Do not synthesize missing transaction IDs or rewrite amounts solely to match the current balance unless a separate migration/contract is adopted.

ProfileStateCopier should either:
- receive a validator-proven source and remain simple, with integration tests proving that contract; or
- defensively skip/contain null history elements as a secondary guard.
In either design, PersistenceCoordinator must not leak an unhandled exception for the H3 fixture.

## 5. Repair evidence

Extend SaveValidationReport with structural repair counters/categories where useful. Logs may name structural categories/IDs but must not dump raw save JSON, filesystem paths, sensor values or sensitive traces.

Required regression style:
- construct or deserialize malformed current-schema material;
- prove the pre-fix unsafe behavior in a focused test where practical;
- run SaveValidator;
- assert repaired canonical shape;
- serialize/reload the repaired profile;
- drive representative service/boot-equivalent access;
- for rollback cases, run a real failing repository through PersistenceCoordinator and assert final state/outcome.

## 6. First-push guard semantics

The race guard must distinguish "ref absent" from "remote unavailable" positively.

Recommended algorithm:
1. Resolve exact target refs/heads/<branch>.
2. Query origin for that exact ref with a command whose exit/status can distinguish no match from transport failure.
3. If the ref exists, fetch the exact ref and require remote tip to be an ancestor of local tip.
4. If the ref is positively absent, permit the first normal push.
5. If remote state cannot be established, refuse.
6. Continue refusing remote deletion and any push that would require force.

PowerShell and shell implementations must share semantics. The pre-push hook must use the same decision model rather than a weaker special case.

Deterministic tests must use local bare remotes only and cover:
- first push to absent branch succeeds;
- second fast-forward push succeeds;
- remote-only advancement blocks;
- diverged local tip blocks;
- deletion blocks;
- unavailable/unreadable remote blocks;
- a similarly named branch does not count as the exact ref.

## 7. M8.6 reconciliation

The executor must not assume d0c8687 is in main.

Startup:
- fetch origin and inspect main;
- inspect origin/agent/walk-game/m8.6-exec-20260826;
- test whether d0c8687 exists locally;
- if it exists and is a descendant of the now-created remote executor branch, a normal guarded push of that prior branch is permitted after inspection;
- never force it;
- compare M8.6 changed files against M8.7 implementation base and preserve equivalent work.

If d0c8687 is unavailable, implement only the still-required M8.6 requirements after proving they are absent on current source.

## 8. Certification evidence tiers

M8.7 preserves the M8.6 evidence hierarchy:
AUTOMATED, STATIC, EDITOR-COMPILE, EDITMODE, PLAYMODE, ANDROID-BUILD, ANDROID-LIFECYCLE, PHYSICAL-SENSOR, PHYSICAL-UX, PERFORMANCE, IOS.

A lower tier never implies a higher one. Missing external prerequisites remain UNVERIFIED.

## 9. Architecture / ADR rule

Update ADR 0007 if the canonical load/repair/rollback contract changes materially. Add a new ADR only if M8.7 introduces a new architecture decision rather than a refinement of ADR 0007. The Git guard fix belongs in agent workflow documentation/tests unless it changes repository governance policy.
