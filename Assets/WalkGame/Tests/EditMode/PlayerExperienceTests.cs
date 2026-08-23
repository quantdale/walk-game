using System;
using System.Linq;
using NUnit.Framework;
using WalkGame.Activity;
using WalkGame.Content;
using WalkGame.Core;
using WalkGame.Gameplay;
using WalkGame.Persistence;

namespace WalkGame.Tests
{
    /// <summary>Regression coverage for the player-facing state surfaces added around the slice.</summary>
    public sealed class PlayerExperienceTests
    {
        private MutableClock _clock;
        private PlayerProfile _profile;
        private AshfallBasinCatalog _catalog;
        private DomainEvents _events;
        private VitalityLedger _ledger;
        private RewardApplier _rewards;
        private RestorationService _restoration;

        [SetUp]
        public void SetUp()
        {
            _clock = new MutableClock(new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc));
            _profile = new PlayerProfile();
            _profile.worldState.currentRegionId = AshfallBasinCatalog.RegionId;
            _catalog = new AshfallBasinCatalog();
            _events = new DomainEvents();
            _ledger = new VitalityLedger(_profile, _clock, _events, Log.Disabled);
            _rewards = new RewardApplier(_profile, _clock, _events, Log.Disabled);
            _restoration = new RestorationService(_catalog, _profile, _ledger, _rewards, _events, Log.Disabled);
            LoadRegion();
        }

        [Test]
        public void ProjectStatus_DistinguishesLockedReadyAndCompleted()
        {
            var first = _restoration.GetStatus("project.ashfall.clear_aqueduct_rubble");
            Assert.AreEqual(RestorationFailure.InsufficientVitality, first.failure);
            Assert.IsFalse(first.IsAvailable);
            Assert.IsFalse(first.IsAffordable);

            _ledger.Credit(VitalityCredit.Steps(100));
            first = _restoration.GetStatus("project.ashfall.clear_aqueduct_rubble");
            Assert.AreEqual(RestorationFailure.None, first.failure);
            Assert.IsTrue(first.IsAvailable);

            Assert.IsTrue(_restoration.TryComplete(first.project.projectId, out var failure), failure.ToString());
            first = _restoration.GetStatus(first.project.projectId);
            Assert.AreEqual(RestorationFailure.AlreadyCompleted, first.failure);
            Assert.IsTrue(first.IsCompleted);

            var downstream = _restoration.GetStatus("project.ashfall.restore_water_station");
            Assert.AreEqual(RestorationFailure.InsufficientVitality, downstream.failure,
                "The status surface should report the first blocking condition in a player-readable order.");
            Assert.AreEqual(15, _restoration.GetStatuses().Count);
        }

        [Test]
        public void EnvironmentFlags_AreCanonicalAndSurviveProjectPresentation()
        {
            _ledger.Credit(VitalityCredit.Steps(10000));
            Assert.IsTrue(_restoration.TryComplete("project.ashfall.clear_aqueduct_rubble", out _));
            Assert.IsTrue(_restoration.TryComplete("project.ashfall.clean_fountain", out _));
            Assert.IsTrue(_restoration.TryComplete("project.ashfall.repair_streetlamps", out _));
            Assert.IsTrue(_restoration.TryComplete("project.ashfall.restore_water_station", out _));
            Assert.IsTrue(_restoration.TryComplete("project.ashfall.refill_river", out _));

            var region = _profile.worldState.GetOrCreateRegionState(AshfallBasinCatalog.RegionId);
            Assert.IsTrue(region.HasEnvironmentFlag(WellKnownIds.EnvironmentFlags.RiverFlowing));
            Assert.IsTrue(region.environmentFlags.Contains(WellKnownIds.EnvironmentFlags.RiverFlowing));
            Assert.IsFalse(region.discoveredLoreIds.Contains("flag:" + WellKnownIds.EnvironmentFlags.RiverFlowing));
            Assert.AreEqual(2, region.restorationStage);
        }

        [Test]
        public void ProductionSummaryAndStatus_ExposeOfflineOutputWithoutChangingMath()
        {
            _ledger.Credit(VitalityCredit.Steps(1000));
            Assert.IsTrue(_restoration.TryComplete("project.ashfall.clear_aqueduct_rubble", out _));
            Assert.IsTrue(_restoration.TryComplete("project.ashfall.restore_water_station", out _));

            var production = new ProductionService(_catalog, _profile, _rewards, _clock, Log.Disabled);
            production.EnsureProducerStates(AshfallBasinCatalog.RegionId);
            _clock.Advance(TimeSpan.FromHours(2));
            var summary = production.AccrueAllWithSummary(AshfallBasinCatalog.RegionId);
            Assert.AreEqual(24, summary.TotalProduced);

            var water = production.GetStatuses(AshfallBasinCatalog.RegionId)
                .Single(status => status.producerId == AshfallBasinCatalog.WaterStationProducer);
            Assert.IsTrue(water.isActive);
            Assert.AreEqual(24, water.storedOutput);
            Assert.AreEqual(12, water.ratePerHour);
            Assert.IsFalse(water.isFull);
        }

        [Test]
        public void ExploreDiscovery_IsGatedAndExactlyOnce()
        {
            var exploration = new ExplorationService(_catalog, _profile, _events);
            Assert.IsFalse(exploration.TryDiscoverLore(AshfallBasinCatalog.RegionId, "lore.ashfall.riverside_letters"));

            _ledger.Credit(VitalityCredit.Steps(10000));
            Assert.IsTrue(_restoration.TryComplete("project.ashfall.clear_aqueduct_rubble", out _));
            Assert.IsTrue(_restoration.TryComplete("project.ashfall.clean_fountain", out _));
            Assert.IsTrue(_restoration.TryComplete("project.ashfall.repair_streetlamps", out _));
            Assert.IsTrue(_restoration.TryComplete("project.ashfall.restore_water_station", out _));
            Assert.IsTrue(_restoration.TryComplete("project.ashfall.refill_river", out _));
            // The river project already reveals this record as part of its reward;
            // Explore may still inspect it, but the canonical discovery is already set.
            Assert.IsFalse(exploration.TryDiscoverLore(AshfallBasinCatalog.RegionId, "lore.ashfall.riverside_letters"));
            Assert.IsTrue(_profile.worldState.GetOrCreateRegionState(AshfallBasinCatalog.RegionId)
                .discoveredLoreIds.Contains("lore.ashfall.riverside_letters"));
            Assert.IsFalse(exploration.TryDiscoverLore(AshfallBasinCatalog.RegionId, "lore.ashfall.riverside_letters"));
        }

        [Test]
        public void BeginExpedition_ClaimsPassiveWindowUntilSessionResult()
        {
            var calculator = new RewardCalculator(RewardPolicy.Default);
            var activity = new ActivityService(
                _profile,
                _ledger,
                new TrustEvaluator(RewardPolicy.Default),
                calculator,
                _events,
                Log.Disabled);

            Assert.IsTrue(activity.BeginExpedition(SessionType.Walk, _clock.UtcNow));
            Assert.IsFalse(activity.BeginExpedition(SessionType.Run, _clock.UtcNow));
            Assert.AreEqual(0, activity.ProcessPassiveSnapshot(new ActivitySnapshot
            {
                providerId = "test",
                intervalStartUtc = _clock.UtcNow.AddMinutes(-1),
                intervalEndUtc = _clock.UtcNow,
                stepCount = 500,
            }));

            var result = activity.ProcessSessionResult(new ActivitySessionResult
            {
                sessionId = "session.experience",
                startUtc = _clock.UtcNow.AddMinutes(-10),
                endUtc = _clock.UtcNow,
                acceptedSteps = 500,
            }, growthEligible: false);
            Assert.AreEqual(500, result.acceptedSteps);
            Assert.IsNull(_profile.activityState.activeSession);
        }

        [Test]
        public void SaveValidator_RepairsNullPresentationCollectionsAndClampsSettings()
        {
            var profile = new PlayerProfile
            {
                resources = null,
                settings = null,
                worldState = new WorldState
                {
                    unlockedRegionIds = null,
                    regionStates = null,
                    currentRegionId = AshfallBasinCatalog.RegionId,
                },
            };

            var report = SaveValidator.RepairAndValidate(profile, _clock, Log.Disabled);

            Assert.IsNotNull(profile.resources);
            Assert.IsNotNull(profile.settings);
            Assert.AreEqual(1f, profile.settings.masterAudioVolume);
            Assert.IsNotNull(profile.worldState.unlockedRegionIds);
            Assert.IsNotNull(profile.worldState.regionStates);
            Assert.IsTrue(profile.worldState.unlockedRegionIds.Contains(AshfallBasinCatalog.RegionId));
            Assert.IsNotNull(report);
        }

        private void LoadRegion()
        {
            var region = _profile.worldState.GetOrCreateRegionState(AshfallBasinCatalog.RegionId);
            foreach (var instance in _catalog.Ashfall.defaultBuildingInstances)
            {
                var state = region.GetOrCreateBuildingState(instance.instanceId, instance.buildingDefinitionId);
                state.placement.gridX = instance.initialPlacement.gridX;
                state.placement.gridY = instance.initialPlacement.gridY;
                state.placement.rotationQuarterTurns = instance.initialPlacement.rotationQuarterTurns;
            }
        }
    }
}
