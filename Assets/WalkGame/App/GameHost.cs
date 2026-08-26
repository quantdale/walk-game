using System;
using UnityEngine;
using WalkGame.Activity;
using WalkGame.Building;
using WalkGame.Content;
using WalkGame.Core;
using WalkGame.Gameplay;
using WalkGame.Persistence;
using WalkGame.World;

namespace WalkGame.App
{
    /// <summary>
    /// Service composition root (TECHNICAL_ARCHITECTURE 6). Owns canonical services and
    /// the loaded profile; persists on pause/quit. This is the single sanctioned global
    /// handle in the codebase - it wires systems, it is not a grab-bag singleton.
    ///
    /// Persistence health contract (ADR 0007): only a genuinely empty save directory
    /// auto-creates a fresh profile. Fatal load states (unreadable material or a newer
    /// schema) boot into a fail-closed recovery mode where no gameplay system exists to
    /// mutate state and lifecycle autosave cannot overwrite the preserved bytes.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class GameHost : MonoBehaviour
    {
        public static GameHost Current { get; private set; }

        /// <summary>
        /// Runtime-test seam for an isolated profile directory. Production leaves this
        /// null and uses Application.persistentDataPath; tests never touch a developer's
        /// real save while exercising restart and reload behavior.
        /// </summary>
        public static string SaveDirectoryOverride { get; set; }

        /// <summary>
        /// Runtime-test seam for deterministic lifecycle and offline-production checks.
        /// Production leaves this null and uses the device UTC clock.
        /// </summary>
        public static IClock ClockOverride { get; set; }

        public IClock Clock { get; private set; }
        public Log Log { get; private set; }
        public DomainEvents Events { get; private set; }
        public AshfallBasinCatalog Catalog { get; private set; }
        public PlayerProfile Profile { get; private set; }
        public VitalityLedger Ledger { get; private set; }
        public RewardApplier Rewards { get; private set; }
        public RestorationService Restoration { get; private set; }
        public ExplorationService Exploration { get; private set; }
        public ProductionService Production { get; private set; }
        public ProductionSummary ResumeProductionSummary { get; private set; } = new ProductionSummary();
        public BuildingPlacementService Placement { get; private set; }
        public ActivityService Activity { get; private set; }
        public IActivityProvider Provider { get; private set; }
        public ModeStateMachine Modes { get; private set; }
        public SaveLoadResult LastSaveResult { get; private set; } = SaveLoadResult.Success;

        /// <summary>Authoritative persistence-health state (ADR 0007).</summary>
        public PersistenceHealth Health { get; private set; } = PersistenceHealth.Fresh;

        public bool PersistenceBlocked => !PersistencePolicy.AllowsDurableMutation(Health);

        /// <summary>Raised after a failed commit reverted canonical state to disk truth.</summary>
        public event Action PersistenceReverted;

        /// <summary>
        /// Raised after every resolved CommitChanges with its durability outcome so
        /// presentation can flush success-only feedback it deferred during the mutation
        /// (domain events fire before the commit is attempted) or drop it on failure.
        /// Not raised on fatal loss: the blocked-mode recomposition replaces the UI.
        /// </summary>
        public event Action<bool> DurableCommitResolved;

        private ISaveRepository _repository;
        private PersistenceCoordinator _coordinator;
        private GameObject _flowGo;
        private GameObject _tickerGo;
        private GameObject _uiGo;
        private GameObject _recoveryGo;

        private void Awake()
        {
            if (Current != null && Current != this)
            {
                Destroy(gameObject);
                return;
            }

            Current = this;
            DontDestroyOnLoad(gameObject);

            Boot();
        }

        private void Boot()
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            Log = new Log(new UnityLogSink(), LogLevel.Debug);
#else
            // Release builds never log sensor-adjacent debug detail.
            Log = new Log(new UnityLogSink(), LogLevel.Warning);
#endif

            Clock = new OffsetClock(ClockOverride ?? SystemClock.Instance);
            Events = new DomainEvents();
            Catalog = new AshfallBasinCatalog();
            WorldRegistry.CurrentCatalog = Catalog;
            WorldRegistry.CurrentRegion = Catalog.Ashfall;

            string saveDirectory = string.IsNullOrWhiteSpace(SaveDirectoryOverride)
                ? Application.persistentDataPath
                : SaveDirectoryOverride;
            _repository = new FileSaveRepository(
                saveDirectory,
                "walkgame.profile.json",
                new JsonSaveSerializer(),
                new SaveMigrator(),
                Log,
                Clock);
            _coordinator = new PersistenceCoordinator(
                _repository,
                Log,
                () =>
                {
                    var pristine = NewProfile();
                    EnsureRegionState(pristine.worldState);
                    return pristine;
                });

            _repository.TryLoad(out var profile, out var result);
            switch (result)
            {
                case SaveLoadResult.Success:
                    Health = PersistenceHealth.Healthy;
                    break;
                case SaveLoadResult.Empty:
                    // Only an empty repository may manufacture a new profile.
                    profile = NewProfile();
                    Health = PersistenceHealth.Fresh;
                    Log.Info("Created fresh player profile.");
                    break;
                case SaveLoadResult.RecoveredFromBackup:
                    Health = PersistenceHealth.Recovered;
                    Log.Warning("Main save was unreadable; recovered last-known-good backup.");
                    break;
                default:
                    // Failed / IncompatibleSchema / RecoveredFromBackupForwardSchema:
                    // fail closed instead of playing against a throwaway profile that
                    // lifecycle autosave could persist over the preserved bytes.
                    Health = PersistenceHealth.Blocked;
                    Log.Warning($"Save material could not be loaded safely ({result}); entering fail-closed recovery mode.");
                    break;
            }

            LastSaveResult = result == default ? SaveLoadResult.Success : result;

            if (PersistenceBlocked)
            {
                Profile = null;
                return;
            }

            Profile = profile;
            Profile.worldState.currentRegionId = WellKnownIds.StartingRegionId;

            BuildServices();
            FinishServiceConstruction();
        }

        /// <summary>Constructs the gameplay service graph bound to the live profile.</summary>
        private void BuildServices()
        {
            Ledger = new VitalityLedger(Profile, Clock, Events, Log);
            Rewards = new RewardApplier(Profile, Clock, Events, Log);
            Restoration = new RestorationService(Catalog, Profile, Ledger, Rewards, Events, Log);
            Exploration = new ExplorationService(Catalog, Profile, Events);
            Production = new ProductionService(Catalog, Profile, Rewards, Clock, Log);
            Placement = new BuildingPlacementService(Catalog, Events);

            var trust = new TrustEvaluator(RewardPolicy.Default);
            var calculator = new RewardCalculator(RewardPolicy.Default);
            Activity = new ActivityService(Profile, Ledger, trust, calculator, Events, Log);
            var milestones = new StepMilestoneService(Catalog, Profile, Ledger, Events);
            Activity.MilestonesPending += _ => milestones.CheckAndAward();

            // A process kill mid-Expedition leaves a persisted suppression marker;
            // recover it here so passive credit cannot stay blocked forever (M8
            // lifecycle red-team). Movement made during the interruption is re-read
            // from the provider cursor through the normal passive stream.
            if (Activity.RecoverInterruptedSession())
            {
                Log.Warning("Stale Expedition marker recovered at boot; passive movement credit resumed.");
            }

            Provider = CreateProvider();
            Modes = new ModeStateMachine(Events, Log);
        }

        /// <summary>Seeds missing region/building/producer state and resumes offline production.</summary>
        private void FinishServiceConstruction()
        {
            EnsureRegionState(Profile.worldState);
            Production.EnsureProducerStates(Profile.worldState.currentRegionId);
            ResumeProductionSummary = Production.AccrueAllWithSummary(Profile.worldState.currentRegionId);

            Modes.TryTransition(GameMode.MainMenu);
        }

        private void Start()
        {
            // Scene composition happens after all Awakes so GameHost.Current is set.
            ComposeRuntimeScene();
        }

        private void ComposeRuntimeScene()
        {
            CreateSunLight();

            if (PersistenceBlocked)
            {
                // Fail-closed recovery surface INSTEAD of the playable runtime: no
                // AppFlow, ticker, HUD, expedition, or debug tool exists to mutate
                // canonical state while saves cannot be loaded safely (ADR 0007).
                _recoveryGo = new GameObject("SaveRecovery");
                _recoveryGo.AddComponent<SaveRecoveryController>();
                return;
            }

            _flowGo = new GameObject("AppFlow");
            var flow = _flowGo.AddComponent<AppFlowController>();

            _tickerGo = new GameObject("ActivityTicker");
            var ticker = _tickerGo.AddComponent<ActivityTicker>();

            _uiGo = new GameObject("UiRoot");
            var composer = _uiGo.AddComponent<UiComposer>();
            composer.Compose(flow, ticker);
        }

        private void ClearComposedRuntime()
        {
            foreach (var composed in new[] { _recoveryGo, _flowGo, _tickerGo, _uiGo })
            {
                if (composed != null)
                {
                    Destroy(composed);
                }
            }

            _recoveryGo = null;
            _flowGo = null;
            _tickerGo = null;
            _uiGo = null;
        }

        private static void CreateSunLight()
        {
            if (FindFirstObjectByType<Light>() != null)
            {
                return;
            }

            var lightGo = new GameObject("Sun");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightGo.transform.rotation = Quaternion.Euler(55f, 35f, 0f);
        }

        private PlayerProfile NewProfile()
        {
            // Creation timestamp flows through the injected clock like every other
            // economic/lifecycle time so debug clock control stays deterministic.
            return new PlayerProfile
            {
                schemaVersion = SaveSchemaVersions.Current,
                createdAtUtc = Clock.UtcNow,
            };
        }

        /// <summary>
        /// The debug provider is explicit in the editor/development harness. A missing
        /// native bridge on a release target is an unavailable movement capability, not
        /// permission to mint debug movement credit.
        /// </summary>
        private IActivityProvider CreateProvider()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                // The profile's activity state seeds the counter reconciler so a
                // process restart resumes from the persisted raw-counter cursor.
                return new WalkGame.Platform.Android.AndroidStepSensorProvider(Clock, Profile.activityState, Log);
            }
            catch (Exception ex)
            {
                // Only genuine bridge/packaging failures land here; missing runtime
                // permission is a normal provider state handled by the permission UI.
                Log.Error($"Android step provider unavailable; movement rewards disabled ({ex.GetType().Name}).");
                return new UnavailableActivityProvider();
            }
#elif UNITY_IOS && !UNITY_EDITOR
            try
            {
                return new WalkGame.Platform.iOS.IosCoreMotionProvider(Clock);
            }
            catch (Exception ex)
            {
                Log.Error($"iOS motion provider unavailable; movement rewards disabled ({ex.GetType().Name}).");
                return new UnavailableActivityProvider();
            }
#endif
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return new DebugActivityProvider(Clock);
#else
            return new UnavailableActivityProvider();
#endif
        }

        private void EnsureRegionState(WorldState world)
        {
            foreach (var regionId in world.unlockedRegionIds)
            {
                if (!world.regionStates.ContainsKey(regionId))
                {
                    world.GetOrCreateRegionState(regionId);
                }
            }

            var region = world.GetOrCreateRegionState(world.currentRegionId);
            foreach (var instance in Catalog.Ashfall.defaultBuildingInstances)
            {
                // Seed only MISSING entries: persisted player placements and lifecycle
                // states must survive every restart untouched (Phase 1 acceptance).
                if (!region.buildingStates.ContainsKey(instance.instanceId))
                {
                    var created = region.GetOrCreateBuildingState(instance.instanceId, instance.buildingDefinitionId);
                    created.placement.gridX = instance.initialPlacement.gridX;
                    created.placement.gridY = instance.initialPlacement.gridY;
                    created.placement.rotationQuarterTurns = instance.initialPlacement.rotationQuarterTurns;
                }
            }
        }

        /// <summary>
        /// Best-effort durable write WITHOUT transactional containment. Reserved for
        /// lifecycle autosave and internal use; player-visible mutations go through
        /// <see cref="CommitChanges"/> so a failed write can never masquerade as durable.
        /// Refuses to touch the failed slot while persistence health is blocked.
        /// </summary>
        public bool Persist()
        {
            if (_repository == null || Profile == null || PersistenceBlocked)
            {
                return false;
            }

            var result = _repository.Save(Profile);
            LastSaveResult = result;
            if (result != SaveLoadResult.Success)
            {
                Log.Error($"Save failed with {result}.");
            }

            return result == SaveLoadResult.Success;
        }

        /// <summary>
        /// Transactional persistence boundary for every durable gameplay mutation
        /// (ADR 0007). Returns the full outcome so application transaction coordinators
        /// can resolve provider deliveries exactly once against proven durability (ADR 0010).
        /// Reverted state is fail-closed and still emits <see cref="PersistenceReverted"/> and
        /// <see cref="DurableCommitResolved"/>(false); fatal loss tears down the runtime and
        /// reports <see cref="PersistenceCommitOutcome.FatalPersistenceLoss"/> without those
        /// commit-resolved events (the blocked recomposition replaces the UI). A blocked
        /// host reports fatal so callers never falsely acknowledge a provider delivery.
        /// </summary>
        public PersistenceCommitOutcome CommitChangesWithOutcome()
        {
            if (PersistenceBlocked || Profile == null)
            {
                return PersistenceCommitOutcome.FatalPersistenceLoss;
            }

            var outcome = _coordinator.Commit(Profile);
            switch (outcome)
            {
                case PersistenceCommitOutcome.Committed:
                    LastSaveResult = SaveLoadResult.Success;
                    DurableCommitResolved?.Invoke(true);
                    return PersistenceCommitOutcome.Committed;
                case PersistenceCommitOutcome.RevertedToLastKnownGood:
                    LastSaveResult = SaveLoadResult.Failed;
                    PersistenceReverted?.Invoke();
                    DurableCommitResolved?.Invoke(false);
                    return PersistenceCommitOutcome.RevertedToLastKnownGood;
                default:
                    EnterBlockedState(_coordinator.LastFailure);
                    return PersistenceCommitOutcome.FatalPersistenceLoss;
            }
        }

        /// <summary>
        /// Transactional persistence boundary for every durable gameplay mutation
        /// (ADR 0007). Returns true only when the mutation is durably committed. On a
        /// write failure the coordinator reverts the live profile graph in place to the
        /// exact last-known-good disk state (or pristine state for never-saved
        /// sessions), keeping every service/actor reference valid; a fatal loss swaps
        /// the runtime into blocked recovery mode. Callers must treat false as "not
        /// saved" and refresh presentation through <see cref="PersistenceReverted"/>.
        /// </summary>
        public bool CommitChanges()
        {
            return CommitChangesWithOutcome() == PersistenceCommitOutcome.Committed;
        }

        /// <summary>In-place retry of the blocked load (e.g. a transient file lock cleared).</summary>
        public bool RetryLoadFromDisk()
        {
            if (!PersistenceBlocked || _repository == null)
            {
                return false;
            }

            if (!_repository.TryLoad(out var profile, out var result))
            {
                return false;
            }

            var recoveredHealth = PersistencePolicy.HealthForBoot(result);
            if (recoveredHealth == PersistenceHealth.Blocked)
            {
                // Still forward-schema evidence; remain fail-closed.
                return false;
            }

            // M8.5 provider lifetime (ADR 0011): the old generation's native monitoring/
            // live session is released BEFORE any service graph is rebuilt, so the new
            // provider cannot inherit duplicate listeners or a leaked AlreadyRunning state.
            ShutdownProvider();

            Health = recoveredHealth;
            LastSaveResult = result;
            Profile = profile;
            Profile.worldState.currentRegionId = WellKnownIds.StartingRegionId;
            BuildServices();
            FinishServiceConstruction();
            Log.Warning("Save became readable again; normal play resumed.");

            ClearComposedRuntime();
            ComposeRuntimeScene();
            return true;
        }

        /// <summary>
        /// Explicit destructive-recovery action behind the recovery UI's two-step
        /// confirmation: quarantines ALL save material byte-for-byte (never deletes),
        /// then boots a genuinely fresh profile. Only reachable from blocked health.
        /// </summary>
        public bool StartOverWithFreshProfile()
        {
            if (!PersistenceBlocked || _repository == null)
            {
                return false;
            }

            _repository.QuarantineAll();
            Log.Warning("Player chose to start over; previous save material was quarantined.");

            // Teardown-before-drop (ADR 0011): release the old provider generation first.
            ShutdownProvider();

            Health = PersistenceHealth.Fresh;
            LastSaveResult = SaveLoadResult.Empty;
            Profile = NewProfile();
            BuildServices();
            FinishServiceConstruction();

            ClearComposedRuntime();
            ComposeRuntimeScene();
            return true;
        }

        /// <summary>Fatal mid-session persistence loss: tear down every mutating system.</summary>
        private void EnterBlockedState(SaveLoadResult reason)
        {
            if (PersistenceBlocked && Profile == null)
            {
                return;
            }

            Health = PersistenceHealth.Blocked;
            LastSaveResult = reason;

            // M8.5 provider lifetime (ADR 0011): shutdown runs BEFORE the graph is
            // dropped so the provider still holds every reference it needs to release
            // native state; it never acknowledges uncommitted movement as durable.
            ShutdownProvider();

            Profile = null;
            Ledger = null;
            Rewards = null;
            Restoration = null;
            Exploration = null;
            Production = null;
            Placement = null;
            Activity = null;
            Provider = null;
            Modes = null;
            ResumeProductionSummary = new ProductionSummary();

            ClearComposedRuntime();
            ComposeRuntimeScene();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                AutosaveForLifecycle();
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                AutosaveForLifecycle();
            }
        }

        private void AutosaveForLifecycle()
        {
            // A blocked session must never overwrite the preserved save material just
            // because the app backgrounded or closed (ADR 0007 acceptance gate 2).
            if (!PersistenceBlocked && Profile != null)
            {
                Persist();
            }
        }

        /// <summary>
        /// Explicit idempotent provider teardown (M8.5 ADR 0011). "Provider = null" is
        /// never the teardown mechanism: every path that drops or rebuilds the service
        /// graph calls this FIRST while the provider instance can still release its own
        /// native monitoring/live-session state. Failures are logged and contained; they
        /// never cause destructive save behavior and never fabricate reward state.
        /// </summary>
        private void ShutdownProvider()
        {
            try
            {
                Provider?.Shutdown();
            }
            catch (Exception ex)
            {
                Log?.Error($"Provider teardown failed ({ex.GetType().Name}); contained without save impact.");
            }
        }

        private void OnDestroy()
        {
            if (Current != this)
            {
                return;
            }

            if (_repository != null && Profile != null && !PersistenceBlocked)
            {
                Persist();
            }

            // Release native provider work last, after the final autosave decision:
            // teardown must not influence what was durably written (ADR 0011).
            ShutdownProvider();
            Current = null;
        }

        private sealed class UnityLogSink : ILog
        {
            public void Log(LogLevel level, string message)
            {
                // hygiene-allow: this sink IS the Log wrapper's engine output adapter.
                switch (level)
                {
                    case LogLevel.Warning: Debug.LogWarning(message); break; // hygiene-allow: sink
                    case LogLevel.Error: Debug.LogError(message); break; // hygiene-allow: sink
                    default: Debug.Log(message); break; // hygiene-allow: sink
                }
            }
        }
    }
}
