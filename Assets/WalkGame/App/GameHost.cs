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
        public ProductionService Production { get; private set; }
        public BuildingPlacementService Placement { get; private set; }
        public ActivityService Activity { get; private set; }
        public IActivityProvider Provider { get; private set; }
        public ModeStateMachine Modes { get; private set; }
        public SaveLoadResult LastSaveResult { get; private set; } = SaveLoadResult.Success;

        private ISaveRepository _repository;

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

            bool fresh = !_repository.TryLoad(out var profile, out var result);
            if (fresh)
            {
                profile = NewProfile();
                Log.Info("Created fresh player profile.");
            }
            else if (result == SaveLoadResult.RecoveredFromBackup)
            {
                Log.Warning("Main save was unreadable; recovered last-known-good backup.");
            }

            LastSaveResult = result == default ? SaveLoadResult.Success : result;
            Profile = profile;
            Profile.worldState.currentRegionId = WellKnownIds.StartingRegionId;

            Ledger = new VitalityLedger(Profile, Clock, Events, Log);
            Rewards = new RewardApplier(Profile, Clock, Events, Log);
            Restoration = new RestorationService(Catalog, Profile, Ledger, Rewards, Events, Log);
            Production = new ProductionService(Catalog, Profile, Rewards, Clock, Log);
            Placement = new BuildingPlacementService(Catalog, Events);

            var trust = new TrustEvaluator(RewardPolicy.Default);
            var calculator = new RewardCalculator(RewardPolicy.Default);
            Activity = new ActivityService(Profile, Ledger, trust, calculator, Events, Log);
            var milestones = new StepMilestoneService(Catalog, Profile, Ledger, Events);
            Activity.MilestonesPending += _ => milestones.CheckAndAward();

            Provider = CreateProvider();

            Modes = new ModeStateMachine(Events, Log);

            EnsureRegionState();
            Production.EnsureProducerStates(Profile.worldState.currentRegionId);
            Production.AccrueAll(Profile.worldState.currentRegionId); // offline window on resume

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

            var flowGo = new GameObject("AppFlow");
            var flow = flowGo.AddComponent<AppFlowController>();

            var tickerGo = new GameObject("ActivityTicker");
            var ticker = tickerGo.AddComponent<ActivityTicker>();

            var uiGo = new GameObject("UiRoot");
            var composer = uiGo.AddComponent<UiComposer>();
            composer.Compose(flow, ticker);
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

        private void EnsureRegionState()
        {
            var world = Profile.worldState;
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

        public bool Persist()
        {
            var result = _repository.Save(Profile);
            LastSaveResult = result;
            if (result != SaveLoadResult.Success)
            {
                Log.Error($"Save failed with {result}.");
            }

            return result == SaveLoadResult.Success;
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                Persist();
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                Persist();
            }
        }

        private void OnDestroy()
        {
            if (Current != this)
            {
                return;
            }

            if (_repository != null && Profile != null)
            {
                Persist();
            }
            Current = null;
        }

        private sealed class UnityLogSink : ILog
        {
            public void Log(LogLevel level, string message)
            {
                switch (level)
                {
                    case LogLevel.Warning: Debug.LogWarning(message); break;
                    case LogLevel.Error: Debug.LogError(message); break;
                    default: Debug.Log(message); break;
                }
            }
        }
    }
}
