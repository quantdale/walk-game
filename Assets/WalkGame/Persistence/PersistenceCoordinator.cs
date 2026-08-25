using System;
using WalkGame.Core;

namespace WalkGame.Persistence
{
    /// <summary>
    /// Application-level persistence health (ADR 0007). One authoritative value
    /// object instead of scattered booleans:
    ///  - Fresh — no save existed; a new profile was created and persistence is allowed.
    ///  - Healthy — the loaded save is durable and normal play proceeds.
    ///  - Recovered — booted from the backup because the main file was corrupt;
    ///    normal play proceeds while the repository protects the trusted recovery copy.
    ///  - Blocked — fatal load state (unreadable, or forward-schema evidence).
    ///    No durable mutation, no lifecycle autosave; only explicit recovery.
    /// </summary>
    public enum PersistenceHealth
    {
        Fresh = 0,
        Healthy = 1,
        Recovered = 2,
        Blocked = 3
    }

    /// <summary>Pure boot/mutation policy over <see cref="SaveLoadResult"/>.</summary>
    public static class PersistencePolicy
    {
        public static PersistenceHealth HealthForBoot(SaveLoadResult result)
        {
            switch (result)
            {
                case SaveLoadResult.Success: return PersistenceHealth.Healthy;
                case SaveLoadResult.Empty: return PersistenceHealth.Fresh;
                case SaveLoadResult.RecoveredFromBackup: return PersistenceHealth.Recovered;
                default:
                    // Failed, IncompatibleSchema, and RecoveredFromBackupForwardSchema all
                    // mean existing save material must not be rewritten by this session.
                    return PersistenceHealth.Blocked;
            }
        }

        public static bool AllowsDurableMutation(PersistenceHealth health)
        {
            return health != PersistenceHealth.Blocked;
        }
    }

    public enum PersistenceCommitOutcome
    {
        Committed = 0,
        RevertedToLastKnownGood = 1,
        FatalPersistenceLoss = 2
    }

    /// <summary>
    /// Transaction boundary between gameplay mutations and disk durability
    /// (TECHNICAL_ARCHITECTURE 10). Commit persists the live canonical profile; when
    /// the write fails it contains the damage by reverting the caller's instance in
    /// place to the exact last-known-good state, or reports a fatal loss so the host
    /// can fail closed. Domain events already emitted during the failed window are
    /// corrected by the caller surfacing truthful failure feedback plus a refresh of
    /// the reverted state — the presentation never keeps claiming success.
    /// </summary>
    public sealed class PersistenceCoordinator
    {
        private readonly ISaveRepository _repository;
        private readonly Log _log;
        private readonly Func<PlayerProfile> _pristineProfileFactory;

        public PersistenceCoordinator(ISaveRepository repository, Log log, Func<PlayerProfile> pristineProfileFactory)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _log = log ?? Log.Disabled;
            _pristineProfileFactory = pristineProfileFactory;
        }

        /// <summary>Load result observed during the most recent failed commit.</summary>
        public SaveLoadResult LastFailure { get; private set; }

        public PersistenceCommitOutcome Commit(PlayerProfile liveProfile)
        {
            if (liveProfile == null)
            {
                throw new ArgumentNullException(nameof(liveProfile));
            }

            var saveResult = _repository.Save(liveProfile);
            if (saveResult == SaveLoadResult.Success)
            {
                return PersistenceCommitOutcome.Committed;
            }

            if (_repository.TryLoad(out var durable, out var loadResult) && durable != null)
            {
                LastFailure = loadResult;
                _log.Error("Persistence failed; reverting in-memory progress to the last-known-good save.");
                ProfileStateCopier.CopyInto(durable, liveProfile);
                return PersistenceCommitOutcome.RevertedToLastKnownGood;
            }

            if (loadResult == SaveLoadResult.Empty && !HasAnySaveMaterial() && _pristineProfileFactory != null)
            {
                // Nothing durable ever existed (fresh session, failing storage): revert
                // to the pristine profile so canonical state equals what disk truthfully holds.
                LastFailure = loadResult;
                _log.Error("Persistence failed with no durable state; reverting session progress.");
                ProfileStateCopier.CopyInto(_pristineProfileFactory(), liveProfile);
                return PersistenceCommitOutcome.RevertedToLastKnownGood;
            }

            LastFailure = loadResult;
            _log.Error($"Persistence failed fatally ({loadResult}); durable progression must be contained.");
            return PersistenceCommitOutcome.FatalPersistenceLoss;
        }

        private bool HasAnySaveMaterial()
        {
            try
            {
                return _repository.MainSaveExists() || _repository.BackupExists();
            }
            catch
            {
                return true; // Cannot prove emptiness; treat as material present (fail closed).
            }
        }
    }
}
