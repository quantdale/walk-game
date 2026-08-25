using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using WalkGame.Activity;
using WalkGame.App;
using WalkGame.Building;
using WalkGame.Content;
using WalkGame.Core;
using WalkGame.Gameplay;
using WalkGame.Persistence;
using WalkGame.World;

namespace WalkGame.Tests.PlayMode
{
    /// <summary>
    /// Unity lifecycle certification for the one-region vertical slice. Domain tests
    /// own rules and arithmetic; this suite proves scene composition, persistence,
    /// canonical projection, async activity scheduling, and permission fallbacks.
    /// </summary>
    public sealed class RuntimeCertificationTests
    {
        private string _testSaveDirectory;
        private MutableClock _clock;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _testSaveDirectory = Path.Combine(
                Application.temporaryCachePath,
                "WalkGameRuntimeCertification",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testSaveDirectory);
            GameHost.SaveDirectoryOverride = _testSaveDirectory;
            _clock = new MutableClock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            GameHost.ClockOverride = _clock;

            if (GameHost.Current != null)
            {
                UnityEngine.Object.Destroy(GameHost.Current.gameObject);
                yield return null;
            }
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (GameHost.Current != null)
            {
                UnityEngine.Object.Destroy(GameHost.Current.gameObject);
                yield return null;
            }

            if (Directory.Exists(_testSaveDirectory))
            {
                Directory.Delete(_testSaveDirectory, recursive: true);
            }

            GameHost.SaveDirectoryOverride = null;
            GameHost.ClockOverride = null;
        }

        [UnityTest]
        public IEnumerator Bootstrap_RestorationActivityPersistence_AndDualViewStayCanonical()
        {
            yield return LoadBootstrapAndWaitForRig();

            var host = GameHost.Current;
            var flow = UnityEngine.Object.FindFirstObjectByType<AppFlowController>();
            var ticker = UnityEngine.Object.FindFirstObjectByType<ActivityTicker>();

            Assert.IsNotNull(host);
            Assert.IsNotNull(flow);
            Assert.IsNotNull(flow.Presenter);
            Assert.IsNotNull(ticker);
            Assert.AreEqual(GameMode.BuilderMode, host.Modes.Current);
            Assert.AreEqual(AshfallBasinCatalog.RegionId, host.Profile.worldState.currentRegionId);
            Assert.AreEqual(9, flow.Presenter.Actors.Count, "Ashfall Basin should hydrate every canonical building instance.");

            // Real runtime-shaped activity path: provider -> ticker -> ActivityService
            // -> VitalityLedger, followed by a restoration transaction.
            var debug = host.Provider as DebugActivityProvider;
            Assert.IsNotNull(debug, "Editor PlayMode must use the explicit debug provider, not a silent sensor fallback.");
            long stepsBefore = host.Profile.lifetimeAcceptedSteps;
            long vitalityBefore = host.Profile.vitalityBalance;
            debug.DebugAddSteps(2400);
            ticker.ProcessPassiveNow();
            yield return null;

            Assert.Greater(host.Profile.lifetimeAcceptedSteps, stepsBefore);
            Assert.Greater(host.Profile.vitalityBalance, vitalityBefore);
            Assert.GreaterOrEqual(host.Profile.vitalityBalance, 190);

            Assert.IsTrue(host.Restoration.TryComplete(
                "project.ashfall.clear_aqueduct_rubble", out var clearFailure), clearFailure.ToString());
            Assert.IsTrue(host.Restoration.TryComplete(
                "project.ashfall.restore_water_station", out var restoreFailure), restoreFailure.ToString());

            var region = host.Profile.worldState.GetOrCreateRegionState(AshfallBasinCatalog.RegionId);
            var water = region.buildingStates[AshfallBasinCatalog.WaterStationInstance];
            Assert.AreEqual(BuildingLifecycleState.Restored, water.lifecycleState);
            flow.Presenter.Refresh();
            Assert.AreEqual(BuildingLifecycleState.Restored,
                flow.Presenter.Actors[AshfallBasinCatalog.WaterStationInstance].AppliedLifecycle);

            // Offline production uses the injected clock, accrues into the producer
            // store, and only grants resources when explicitly collected.
            _clock.Advance(TimeSpan.FromHours(2));
            var producer = region.producerStates[AshfallBasinCatalog.WaterStationProducer];
            var production = host.Production.Accrue(AshfallBasinCatalog.RegionId, producer);
            Assert.AreEqual(24, production.produced, "Water station should produce 12/hour for two fake hours.");
            Assert.AreEqual(24, producer.storedOutput);
            var collected = host.Production.Collect(
                AshfallBasinCatalog.RegionId,
                AshfallBasinCatalog.WaterStationProducer);
            Assert.AreEqual(24, collected.collected);
            Assert.AreEqual(24, host.Profile.resources[WellKnownIds.Resources.Water]);

            // Mandatory Builder -> save -> reload -> Explore projection scenario.
            Assert.AreEqual(PlacementFailure.None, host.Placement.BeginMove(
                host.Catalog.Ashfall, region, AshfallBasinCatalog.WaterStationInstance));
            var candidate = new BuildingPlacement { gridX = 7, gridY = 7, rotationQuarterTurns = 1 };
            Assert.AreEqual(PlacementFailure.None, host.Placement.PreviewCandidate(candidate));
            Assert.IsTrue(host.Placement.ConfirmMove(candidate, out var placementFailure), placementFailure.ToString());
            flow.Presenter.Refresh();

            var committed = region.buildingStates[AshfallBasinCatalog.WaterStationInstance].placement;
            Assert.AreEqual(7, committed.gridX);
            Assert.AreEqual(7, committed.gridY);
            Assert.AreEqual(1, committed.rotationQuarterTurns);
            int width;
            int depth;
            BuildingPlacementService.GetFootprintExtent(
                host.Catalog.GetBuilding(water.definitionId), committed.rotationQuarterTurns, out width, out depth);
            var expectedPosition = new Vector3(
                host.Catalog.Ashfall.placementOriginX + committed.gridX + width * 0.5f,
                0f,
                host.Catalog.Ashfall.placementOriginY + committed.gridY + depth * 0.5f);
            var actor = flow.Presenter.Actors[AshfallBasinCatalog.WaterStationInstance];
            AssertVectorApproximately(expectedPosition, actor.transform.position);
            Assert.Less(Quaternion.Angle(Quaternion.Euler(0f, 90f, 0f), actor.transform.rotation), 0.01f);

            long savedSteps = host.Profile.lifetimeAcceptedSteps;
            long savedVitality = host.Profile.vitalityBalance;
            long savedWater = host.Profile.resources[WellKnownIds.Resources.Water];
            Assert.IsTrue(host.Persist());

            UnityEngine.Object.Destroy(host.gameObject);
            yield return WaitForHostToClear();
            yield return LoadBootstrapAndWaitForRig();

            host = GameHost.Current;
            flow = UnityEngine.Object.FindFirstObjectByType<AppFlowController>();
            region = host.Profile.worldState.GetOrCreateRegionState(AshfallBasinCatalog.RegionId);
            water = region.buildingStates[AshfallBasinCatalog.WaterStationInstance];
            committed = water.placement;
            Assert.AreEqual(savedSteps, host.Profile.lifetimeAcceptedSteps);
            Assert.AreEqual(savedVitality, host.Profile.vitalityBalance);
            Assert.AreEqual(savedWater, host.Profile.resources[WellKnownIds.Resources.Water]);
            Assert.AreEqual(BuildingLifecycleState.Restored, water.lifecycleState);
            Assert.AreEqual(7, committed.gridX);
            Assert.AreEqual(7, committed.gridY);
            Assert.AreEqual(1, committed.rotationQuarterTurns);

            actor = flow.Presenter.Actors[AshfallBasinCatalog.WaterStationInstance];
            AssertVectorApproximately(expectedPosition, actor.transform.position);
            Assert.Less(Quaternion.Angle(Quaternion.Euler(0f, 90f, 0f), actor.transform.rotation), 0.01f);

            flow.EnterExplore();
            yield return null;
            Assert.AreEqual(GameMode.ExploreMode, host.Modes.Current);
            AssertVectorApproximately(expectedPosition, actor.transform.position);
            flow.EnterBuilder();
            yield return null;
            Assert.AreEqual(GameMode.BuilderMode, host.Modes.Current);
            Assert.AreEqual(7, region.buildingStates[AshfallBasinCatalog.WaterStationInstance].placement.gridX);
            Assert.AreEqual(1, region.buildingStates[AshfallBasinCatalog.WaterStationInstance].placement.rotationQuarterTurns);

            // Permission denial is a normal provider state and must not block the
            // rest of the runtime mode flow.
            debug = host.Provider as DebugActivityProvider;
            debug.DebugSetPermission(false);
            var permissions = new MotionPermissionCoordinator(debug, host.Log);
            var refreshTask = permissions.RefreshAsync();
            while (!refreshTask.IsCompleted)
            {
                yield return null;
            }

            Assert.AreEqual(ActivityPermissionState.Denied, refreshTask.GetAwaiter().GetResult());
            var requestTask = permissions.RequestAsync();
            while (!requestTask.IsCompleted)
            {
                yield return null;
            }

            Assert.AreEqual(MotionPermissionOutcome.Denied, requestTask.GetAwaiter().GetResult());
            flow.EnterExplore();
            yield return null;
            flow.EnterBuilder();
            yield return null;
            Assert.AreEqual(GameMode.BuilderMode, host.Modes.Current);
            Assert.IsTrue(host.Persist());
        }

        [UnityTest]
        public IEnumerator Bootstrap_ComposesUiRuntimeWithEventSystem()
        {
            yield return LoadBootstrapAndWaitForRig();

            // M8 first-import defect regression: programmatic UI rendered but every
            // button/joystick was inert because nothing created an EventSystem.
            Assert.IsNotNull(UnityEngine.EventSystems.EventSystem.current,
                "Bootstrap composition must provide an EventSystem for uGUI input.");
        }

        [UnityTest]
        public IEnumerator StaleExpeditionMarker_FromProcessDeath_RecoversAtBootAndPassiveCreditResumes()
        {
            // Persist a profile that died mid-Expedition: the suppression marker is on disk.
            var clock = new MutableClock(new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc));
            var deadProfile = new PlayerProfile { schemaVersion = SaveSchemaVersions.Current, createdAtUtc = clock.UtcNow };
            deadProfile.activityState.activeSession = new ActiveSessionState
            {
                sessionType = SessionType.Walk,
                startedAtUtc = clock.UtcNow.AddMinutes(-2),
            };
            var repository = new FileSaveRepository(
                _testSaveDirectory, "walkgame.profile.json",
                new JsonSaveSerializer(), new SaveMigrator(),
                Log.Disabled, clock);
            Assert.AreEqual(SaveLoadResult.Success, repository.Save(deadProfile));

            yield return LoadBootstrapAndWaitForRig();

            var host = GameHost.Current;
            Assert.IsNull(host.Profile.activityState.activeSession,
                "boot must recover the stale Expedition marker.");

            var debug = host.Provider as DebugActivityProvider;
            Assert.IsNotNull(debug);
            long stepsBefore = host.Profile.lifetimeAcceptedSteps;
            long vitalityBefore = host.Profile.vitalityBalance;
            debug.DebugAddSteps(1500);
            UnityEngine.Object.FindFirstObjectByType<ActivityTicker>().ProcessPassiveNow();
            yield return null;

            Assert.Greater(host.Profile.lifetimeAcceptedSteps, stepsBefore,
                "passive movement credit must resume after interrupted-session recovery.");
            Assert.Greater(host.Profile.vitalityBalance, vitalityBefore);
        }

        [UnityTest]
        public IEnumerator CorruptSaves_BootIntoFailClosedRecovery_LifecyclePreservesBytes()
        {
            string mainPath = Path.Combine(_testSaveDirectory, "walkgame.profile.json");
            string backupPath = mainPath + ".bak";
            File.WriteAllText(mainPath, "{ broken main bytes");
            File.WriteAllText(backupPath, "{ broken backup bytes");
            byte[] mainBefore = File.ReadAllBytes(mainPath);
            byte[] backupBefore = File.ReadAllBytes(backupPath);

            yield return LoadBootstrapAndWaitForHost(host => host != null && host.PersistenceBlocked);

            var host = GameHost.Current;
            Assert.IsNull(host.Profile, "blocked boot must not fabricate a playable profile");
            Assert.IsNull(UnityEngine.Object.FindFirstObjectByType<AppFlowController>(),
                "no playable rig may compose while persistence health is blocked");

            // Acceptance gate 2: shutdown autosave must never overwrite preserved bytes.
            UnityEngine.Object.Destroy(host.gameObject);
            yield return WaitForHostToClear();
            CollectionAssert.AreEqual(mainBefore, File.ReadAllBytes(mainPath), "main slot untouched");
            CollectionAssert.AreEqual(backupBefore, File.ReadAllBytes(backupPath), "backup slot untouched");
        }

        [UnityTest]
        public IEnumerator BlockedBoot_StartOver_QuarantinesEvidence_AndRecomposesPlayableRuntime()
        {
            string mainPath = Path.Combine(_testSaveDirectory, "walkgame.profile.json");
            string backupPath = mainPath + ".bak";
            File.WriteAllText(mainPath, "{ broken main bytes");
            File.WriteAllText(backupPath, "{ broken backup bytes");

            yield return LoadBootstrapAndWaitForHost(host => host != null && host.PersistenceBlocked);
            var host = GameHost.Current;

            Assert.IsTrue(host.StartOverWithFreshProfile());
            Assert.IsNotNull(host.Profile, "explicit start-over creates a genuinely fresh profile");
            Assert.IsFalse(host.PersistenceBlocked);
            Assert.AreEqual(PersistenceHealth.Fresh, host.Health);

            // Destructive recovery quarantines instead of deleting (acceptance gate 3).
            Assert.IsTrue(File.Exists(mainPath + ".quarantined"), "main evidence preserved");
            Assert.IsTrue(File.Exists(backupPath + ".quarantined"), "backup evidence preserved");
            StringAssert.StartsWith("{ broken", File.ReadAllText(mainPath + ".quarantined"));

            // The playable runtime must come back composed for the fresh profile.
            for (int frame = 0; frame < 180; frame++)
            {
                if (UnityEngine.Object.FindFirstObjectByType<AppFlowController>() != null &&
                    host.Modes != null && host.Modes.Current == GameMode.BuilderMode)
                {
                    break;
                }

                yield return null;
            }

            Assert.IsNotNull(UnityEngine.Object.FindFirstObjectByType<AppFlowController>(),
                "fresh session recomposes the playable runtime");
        }

        private IEnumerator LoadBootstrapAndWaitForRig()
        {
            var operation = SceneManager.LoadSceneAsync("Assets/WalkGame/Core/Bootstrap.unity", LoadSceneMode.Single);
            Assert.IsNotNull(operation);
            while (!operation.isDone)
            {
                yield return null;
            }

            for (int frame = 0; frame < 180; frame++)
            {
                var flow = UnityEngine.Object.FindFirstObjectByType<AppFlowController>();
                if (GameHost.Current != null && flow != null && flow.Presenter != null &&
                    GameHost.Current.Modes != null && GameHost.Current.Modes.Current == GameMode.BuilderMode)
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail("Bootstrap did not compose GameHost, AppFlow, and the Builder rig within 180 frames.");
        }

        private IEnumerator LoadBootstrapAndWaitForHost(System.Func<GameHost, bool> ready)
        {
            var operation = SceneManager.LoadSceneAsync("Assets/WalkGame/Core/Bootstrap.unity", LoadSceneMode.Single);
            Assert.IsNotNull(operation);
            while (!operation.isDone)
            {
                yield return null;
            }

            for (int frame = 0; frame < 180; frame++)
            {
                if (ready(GameHost.Current))
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail("Bootstrap did not reach the expected host state within 180 frames.");
        }

        private IEnumerator WaitForHostToClear()
        {
            for (int frame = 0; frame < 60 && GameHost.Current != null; frame++)
            {
                yield return null;
            }

            Assert.IsNull(GameHost.Current, "GameHost singleton must clear on destruction so a reload can compose a fresh host.");
        }

        private static void AssertVectorApproximately(Vector3 expected, Vector3 actual)
        {
            Assert.Less(Vector3.Distance(expected, actual), 0.001f,
                $"Expected {expected} but got {actual}.");
        }
    }
}
