# ADR 0007 — Fail-closed save integrity and transactional persistence

## Status

Accepted

## Context

The M8.1 campaign audit (planned from `main@0cdf823`) found a gap between the
documented durability guarantees and application behavior:

1. `GameHost.Boot()` treated every failed `TryLoad` as "no save" and manufactured a
   fresh playable profile. A corrupt main + corrupt backup, or a newer-schema save,
   booted into a normal session that lifecycle autosave (`OnApplicationFocus/Pause/
   OnDestroy`) would then persist **over the preserved failed bytes**.
2. The repository rotation algorithm copied main→backup before replacing main without
   checking whether main was readable. After booting from a backup recovery, the first
   save copied the **corrupt** main over the **trusted** backup; an interruption in
   that window destroyed the last-known-good copy.
3. Gameplay mutations called persistence fire-and-forget, so a failed write left UI
   claiming success while disk held older state — violating TECHNICAL_ARCHITECTURE §10.
4. Save-failure copy told players their "session is still playable", which was false:
   nothing they did could be durably committed.
5. A newer-schema main could be silently rewritten by an older build after recovering
   from an older backup.

## Decision

1. **One explicit health state.** `PersistenceHealth` (`Fresh | Healthy | Recovered |
   Blocked`) derived purely from `SaveLoadResult` via `PersistencePolicy`. Only
   `Empty` auto-creates a profile; `RecoveredFromBackupForwardSchema` (new result)
   joins `Failed`/`IncompatibleSchema` as fail-closed states.
2. **Blocked means no mutation surface exists.** In blocked health the composition
   root builds no gameplay services, no activity ticker, no flow rig, no HUD — it
   composes only `SaveRecoveryController`. Lifecycle autosave is gated on health.
3. **Trusted rotation.** `FileSaveRepository.Save` reads back the main slot before
   rotating: readable main → classic rotation; forward-schema main → refuse the save;
   corrupt main → quarantine it byte-for-byte to `<slot>.quarantined`, seed the backup
   from the validated payload BEFORE touching main. At every injected interruption
   point at least one trusted copy survives, and failed material is never deleted.
4. **Transactional commits.** All player-visible durable mutations go through
   `GameHost.CommitChanges()` → `PersistenceCoordinator.Commit`: on write failure the
   canonical profile graph is reverted IN PLACE to exact disk truth via
   `ProfileStateCopier` (hand-written, IL2CPP-safe, reference-preserving), or the host
   enters blocked state on fatal loss. Callers treat `false` as "not saved" and
   suppress success-only feedback.
5. **Explicit recovery.** The recovery screen offers an in-place load retry and a
   two-tap-confirmed "start over" that quarantines all save material before creating
   a fresh profile. Player copy never exposes paths, stack traces, or native errors.

## Consequences

- Loss of player progress becomes structurally difficult: no path can autosave a
  throwaway or rolled-back state over preserved evidence, and interrupted saves can
  no longer destroy the last trusted copy.
- Exactly-once activity semantics are strengthened: rollbacks restore cursors/dedup
  stores together with balances, keeping replay idempotent.
- New save-slot artifacts (`.quarantined` files) appear next to existing saves.
  Amendment (M8.2): the `DeleteAll` repository API was removed entirely rather than
  debug-gated — quarantine is the only sanctioned destructive path, and a bulk
  delete method invited future callers to bypass that semantics.
- `ProfileStateCopier` must be extended alongside any DATA_MODEL model change; a
  serialized-graph fidelity test enforces this (`SaveIntegrityApplicationTests`).
- EDITOR-tier behavior of the blocked boot/recovery UI is committed but remains
  UNVERIFIED until a licensed editor run (see IMPLEMENTATION_STATUS).

## M8.7 amendment — canonical-state structural repair (no schema bump)

M8.7 covers a new defect family: schema-compatible JSON can carry null or identity-inconsistent elements that survive the validator and crash later boot or rollback.

- **Decision:** `SaveValidator.RepairAndValidate` now deterministically repairs the full canonical graph without a schema bump: null `RegionState` values are reconstructed from the authoritative dictionary key when the key is required (current or unlocked) or pruned when unreachable; `RegionState.regionId` is normalized to the dictionary key so storage identity is coherent; null `VitalityTransaction` elements are pruned without changing `vitalityBalance`. `WorldState.GetOrCreateRegionState` self-heals an existing null value; `ProfileStateCopier` defensively skips null history elements on the rollback boundary. `SaveValidationReport` exposes counters for each repair. Repair never mints progression, completes projects, or awards milestones, and re-repair is idempotent with round-trip fidelity.
- **Consequences:** Hand-edited or partially migrated saves that previously crashed boot (`GetOrCreateRegionState` returning null) or rollback (`CopyTransaction` NRE) now converge to a canonical structural shape or remain fail-closed only when truth cannot be reconstructed. The new `M87SaveIntegrityClosureTests` and the existing copier-fidelity tests lock this contract.
