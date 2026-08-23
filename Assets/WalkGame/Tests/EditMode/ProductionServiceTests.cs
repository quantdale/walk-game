using System;
using NUnit.Framework;
using WalkGame.Core;
using WalkGame.Gameplay;

namespace WalkGame.Tests
{
    public sealed class ProductionServiceTests
    {
        private MutableClock _clock;
        private PlayerProfile _profile;
        private DomainEvents _events;
        private ContentCatalog _catalog;
        private RewardApplier _rewards;
        private ProductionService _production;

        [SetUp]
        public void SetUp()
        {
            _clock = new MutableClock(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));
            _profile = new PlayerProfile();
            _events = new DomainEvents();
            _catalog = TestContent.Create();
            _catalog.Index();
            _rewards = new RewardApplier(_profile, _clock, _events, Log.Disabled);
            _production = new ProductionService(_catalog, _profile, _rewards, _clock, Log.Disabled);

            var region = _profile.worldState.GetOrCreateRegionState(TestContent.RegionId);
            var pump = region.GetOrCreateBuildingState(TestContent.PumpInstanceId, "building.pump_station");
            pump.lifecycleState = BuildingLifecycleState.Restored;
            pump.upgradeTier = 1;
            region.producerStates[TestContent.PumpProducerId] = new ProducerState
            {
                producerId = TestContent.PumpProducerId,
                buildingInstanceId = TestContent.PumpInstanceId,
                lastCheckpointUtc = _clock.UtcNow,
            };
        }

        [Test]
        public void ProducesDeterministically_FromCheckpoint()
        {
            var region = _profile.worldState.GetOrCreateRegionState(TestContent.RegionId);
            var producer = region.producerStates[TestContent.PumpProducerId];

            _clock.Advance(TimeSpan.FromHours(2));
            var result = _production.Accrue(TestContent.RegionId, producer);

            Assert.AreEqual(20, result.produced); // 10/h * 2h
            Assert.AreEqual(20, producer.storedOutput);
        }

        [Test]
        public void OfflineWindow_IsCapped()
        {
            var region = _profile.worldState.GetOrCreateRegionState(TestContent.RegionId);
            var producer = region.producerStates[TestContent.PumpProducerId];

            _clock.Advance(TimeSpan.FromHours(100)); // far beyond the 8h cap
            var result = _production.Accrue(TestContent.RegionId, producer);

            Assert.IsTrue(result.cappedByOfflineWindow);
            Assert.AreEqual(80, result.produced); // 10/h * 8h cap
        }

        [Test]
        public void BackwardClock_NeverProducesNegative()
        {
            var region = _profile.worldState.GetOrCreateRegionState(TestContent.RegionId);
            var producer = region.producerStates[TestContent.PumpProducerId];

            _clock.Advance(TimeSpan.FromHours(-3));
            var result = _production.Accrue(TestContent.RegionId, producer);

            Assert.IsTrue(result.clockAnomaly);
            Assert.AreEqual(0, result.produced);
            Assert.AreEqual(0, producer.storedOutput);
        }

        [Test]
        public void RuinedBuilding_ProducesNothing_AndRebaselinesCheckpoint()
        {
            var region = _profile.worldState.GetOrCreateRegionState(TestContent.RegionId);
            var producer = region.producerStates[TestContent.PumpProducerId];
            region.buildingStates[TestContent.PumpInstanceId].lifecycleState = BuildingLifecycleState.Ruin;

            _clock.Advance(TimeSpan.FromHours(5));
            var result = _production.Accrue(TestContent.RegionId, producer);

            Assert.AreEqual(0, result.produced);
            Assert.AreEqual(_clock.UtcNow, producer.lastCheckpointUtc);

            // Restoring later starts production fresh - no retroactive backlog dump.
            region.buildingStates[TestContent.PumpInstanceId].lifecycleState = BuildingLifecycleState.Restored;
            _clock.Advance(TimeSpan.FromHours(1));
            var next = _production.Accrue(TestContent.RegionId, producer);
            Assert.AreEqual(10, next.produced);
        }

        [Test]
        public void StorageCap_BoundsStoredOutput()
        {
            var region = _profile.worldState.GetOrCreateRegionState(TestContent.RegionId);
            var producer = region.producerStates[TestContent.PumpProducerId];

            for (int i = 0; i < 200; i++)
            {
                _clock.Advance(TimeSpan.FromHours(8));
                _production.Accrue(TestContent.RegionId, producer);
            }

            Assert.AreEqual(1000, producer.storedOutput); // definition storageCap
        }

        [Test]
        public void Collect_GrantsResource_AndResetsStore()
        {
            var region = _profile.worldState.GetOrCreateRegionState(TestContent.RegionId);
            var producer = region.producerStates[TestContent.PumpProducerId];

            _clock.Advance(TimeSpan.FromHours(3));
            _production.AccrueAll(TestContent.RegionId);

            var collected = _production.Collect(TestContent.RegionId, TestContent.PumpProducerId);

            Assert.AreEqual(30, collected.collected);
            Assert.AreEqual(WellKnownIds.Resources.Water, collected.resourceId);
            Assert.AreEqual(30, _profile.resources[WellKnownIds.Resources.Water]);
            Assert.AreEqual(0, producer.storedOutput);
        }

        [Test]
        public void UpgradeTier_MultipliesProduction()
        {
            var region = _profile.worldState.GetOrCreateRegionState(TestContent.RegionId);

            // Tier multiplier comes from the producer definition (tier map), tier 2 -> 1.5x.
            region.buildingStates[TestContent.PumpInstanceId].upgradeTier = 2;

            _clock.Advance(TimeSpan.FromHours(2));
            var result = _production.Accrue(TestContent.RegionId, region.producerStates[TestContent.PumpProducerId]);

            Assert.AreEqual(30, result.produced); // 10 * 1.5 * 2
        }

        [Test]
        public void PendingCollectables_ListNonEmptyStores_AndClearAfterCollect()
        {
            var region = _profile.worldState.GetOrCreateRegionState(TestContent.RegionId);

            Assert.AreEqual(0, _production.GetPendingCollectables(TestContent.RegionId).Count);

            _clock.Advance(TimeSpan.FromHours(3));
            _production.AccrueAll(TestContent.RegionId);

            var pending = _production.GetPendingCollectables(TestContent.RegionId);
            Assert.AreEqual(1, pending.Count);
            Assert.AreEqual(TestContent.PumpProducerId, pending[0].producerId);
            Assert.AreEqual(WellKnownIds.Resources.Water, pending[0].resourceId);
            Assert.AreEqual(30, pending[0].stored);

            _production.Collect(TestContent.RegionId, TestContent.PumpProducerId);
            Assert.AreEqual(0, _production.GetPendingCollectables(TestContent.RegionId).Count);
        }

        [Test]
        public void CollectAll_GrantsEveryStore_AndEmptiesThem()
        {
            const string compostProducerId = "producer.test.compost";
            _catalog.Producers.Add(new ProducerDefinition
            {
                producerId = compostProducerId,
                resourceId = WellKnownIds.Resources.Biomass,
                baseRatePerHour = 5.0,
                storageCap = 500,
                offlineCapHours = 8.0,
                tierMultipliers = { { 1, 1.0 } },
            });
            _catalog.Index();

            var region = _profile.worldState.GetOrCreateRegionState(TestContent.RegionId);
            var grove = region.GetOrCreateBuildingState(TestContent.GroveInstanceId, "building.grove");
            grove.lifecycleState = BuildingLifecycleState.Restored;
            region.producerStates[compostProducerId] = new ProducerState
            {
                producerId = compostProducerId,
                buildingInstanceId = TestContent.GroveInstanceId,
                lastCheckpointUtc = _clock.UtcNow,
            };

            _clock.Advance(TimeSpan.FromHours(3));
            _production.AccrueAll(TestContent.RegionId); // pump: 30 water, compost: 15 biomass

            var results = _production.CollectAll(TestContent.RegionId);

            Assert.AreEqual(2, results.Count);
            Assert.AreEqual(30, _profile.resources[WellKnownIds.Resources.Water]);
            Assert.AreEqual(15, _profile.resources[WellKnownIds.Resources.Biomass]);
            Assert.AreEqual(0, region.producerStates[TestContent.PumpProducerId].storedOutput);
            Assert.AreEqual(0, region.producerStates[compostProducerId].storedOutput);
            Assert.AreEqual(0, _production.GetPendingCollectables(TestContent.RegionId).Count);
        }
    }
}
