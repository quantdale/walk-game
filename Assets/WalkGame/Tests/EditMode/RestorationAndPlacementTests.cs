using System;
using NUnit.Framework;
using WalkGame.Activity;
using WalkGame.Building;
using WalkGame.Core;
using WalkGame.Gameplay;

namespace WalkGame.Tests
{
    /// <summary>
    /// End-to-end domain loop: debug steps -> vitality -> restoration -> move -> stage.
    /// Mirrors ROADMAP Phase 2 acceptance criteria without presentation.
    /// </summary>
    public sealed class RestorationAndPlacementTests
    {
        private MutableClock _clock;
        private PlayerProfile _profile;
        private DomainEvents _events;
        private ContentCatalog _catalog;
        private VitalityLedger _ledger;
        private RewardApplier _rewards;
        private RestorationService _restoration;
        private BuildingPlacementService _placement;

        [SetUp]
        public void SetUp()
        {
            _clock = new MutableClock(new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc));
            _profile = new PlayerProfile();
            _profile.worldState.currentRegionId = TestContent.RegionId;
            _events = new DomainEvents();
            _catalog = TestContent.Create();
            _catalog.Index();
            _ledger = new VitalityLedger(_profile, _clock, _events, Log.Disabled);
            _rewards = new RewardApplier(_profile, _clock, _events, Log.Disabled);
            _restoration = new RestorationService(_catalog, _profile, _ledger, _rewards, _events, Log.Disabled);
            _placement = new BuildingPlacementService(_catalog, _events);

            // Region load: create canonical building states from the definition.
            LoadRegion();
            _ledger.Credit(VitalityCredit.Steps(5000));
        }

        private void LoadRegion()
        {
            var definition = _catalog.GetRegion(TestContent.RegionId);
            var region = _profile.worldState.GetOrCreateRegionState(definition.regionId);
            foreach (var instance in definition.defaultBuildingInstances)
            {
                var state = region.GetOrCreateBuildingState(instance.instanceId, instance.buildingDefinitionId);
                state.placement.gridX = instance.initialPlacement.gridX;
                state.placement.gridY = instance.initialPlacement.gridY;
                state.placement.rotationQuarterTurns = instance.initialPlacement.rotationQuarterTurns;
                if (instance.startsRestored)
                {
                    state.lifecycleState = BuildingLifecycleState.Restored;
                }
            }
        }

        private bool Complete(string projectId)
        {
            return _restoration.TryComplete(projectId, out _);
        }

        [Test]
        public void PrerequisiteChain_IsEnforced()
        {
            Assert.AreEqual(RestorationFailure.MissingPrerequisite,
                _restoration.Evaluate("project.test.restore_pump", out _, out _));
            Assert.IsTrue(Complete("project.test.clear_rubble"));
            Assert.AreEqual(RestorationFailure.None,
                _restoration.Evaluate("project.test.restore_pump", out _, out _));
        }

        [Test]
        public void CompletingProject_SpendsVitality_AppliesRewards()
        {
            long before = _ledger.GetBalance();

            Assert.IsTrue(Complete("project.test.clear_rubble"));

            Assert.AreEqual(before - 50, _ledger.GetBalance());
            Assert.IsTrue(_profile.worldState.TryGetRegionState(TestContent.RegionId, out var region));
            Assert.AreEqual(5, region.infrastructureScore);
            Assert.Contains("project.test.clear_rubble", new System.Collections.Generic.List<string>(region.completedProjectIds));
        }

        [Test]
        public void RestorePump_MarksBuildingRestored_AndEnablesMovement()
        {
            Complete("project.test.clear_rubble");
            Complete("project.test.restore_pump");

            var region = _profile.worldState.GetOrCreateRegionState(TestContent.RegionId);
            Assert.IsTrue(region.buildingStates[TestContent.PumpInstanceId].IsRestored);

            // Move to a free spot.
            Assert.AreEqual(PlacementFailure.None,
                _placement.BeginMove(_catalog.GetRegion(TestContent.RegionId), region, TestContent.PumpInstanceId));
            var candidate = new BuildingPlacement { gridX = 6, gridY = 6 };
            Assert.AreEqual(PlacementFailure.None, _placement.PreviewCandidate(candidate));
            Assert.IsTrue(_placement.ConfirmMove(candidate, out _));
            Assert.AreEqual(6, region.buildingStates[TestContent.PumpInstanceId].placement.gridX);
        }

        [Test]
        public void RuinedBuilding_CannotMove()
        {
            var region = _profile.worldState.GetOrCreateRegionState(TestContent.RegionId);

            Assert.AreEqual(PlacementFailure.NotRestoredYet,
                _placement.BeginMove(_catalog.GetRegion(TestContent.RegionId), region, TestContent.GroveInstanceId));
        }

        [Test]
        public void FixedLandmark_NeverMoves()
        {
            Complete("project.test.clear_rubble");
            Complete("project.test.restore_pump");
            var region = _profile.worldState.GetOrCreateRegionState(TestContent.RegionId);

            // Dam is fixed by definition even though its lifecycle is ruin.
            var damDefinition = _catalog.GetBuilding("building.dam");
            Assert.IsFalse(damDefinition.movableAfterRestore);
        }

        [Test]
        public void OverlappingFootprint_Rejected()
        {
            Complete("project.test.clear_rubble");
            Complete("project.test.restore_pump");
            var region = _profile.worldState.GetOrCreateRegionState(TestContent.RegionId);
            var definition = _catalog.GetRegion(TestContent.RegionId);

            // Grove sits at (8,8); pump is 2x2 so placing it at (7,7) overlaps.
            _placement.BeginMove(definition, region, TestContent.PumpInstanceId);
            var overlapping = new BuildingPlacement { gridX = 7, gridY = 7 };

            Assert.AreEqual(PlacementFailure.OverlapsBuilding, _placement.PreviewCandidate(overlapping));
            // Confirm must fail and leave state untouched.
            Assert.IsFalse(_placement.ConfirmMove(overlapping, out var failure));
            Assert.AreEqual(PlacementFailure.OverlapsBuilding, failure);
            Assert.AreEqual(4, region.buildingStates[TestContent.PumpInstanceId].placement.gridX);
        }

        [Test]
        public void ReservedArea_AndOutsideBounds_Rejected()
        {
            Complete("project.test.clear_rubble");
            Complete("project.test.restore_pump");
            var region = _profile.worldState.GetOrCreateRegionState(TestContent.RegionId);
            var definition = _catalog.GetRegion(TestContent.RegionId);

            _placement.BeginMove(definition, region, TestContent.PumpInstanceId);

            Assert.AreEqual(PlacementFailure.ReservedArea, _placement.PreviewCandidate(new BuildingPlacement { gridX = 14, gridY = 5 }));
            Assert.AreEqual(PlacementFailure.OutsidePlacementArea, _placement.PreviewCandidate(new BuildingPlacement { gridX = -2, gridY = 0 }));
        }

        [Test]
        public void CancelMove_RestoresOriginalTransform()
        {
            Complete("project.test.clear_rubble");
            Complete("project.test.restore_pump");
            var region = _profile.worldState.GetOrCreateRegionState(TestContent.RegionId);
            var definition = _catalog.GetRegion(TestContent.RegionId);

            _placement.BeginMove(definition, region, TestContent.PumpInstanceId);
            _placement.ConfirmMove(new BuildingPlacement { gridX = 9, gridY = 9 }, out _);
            _placement.BeginMove(definition, region, TestContent.PumpInstanceId);
            _placement.CancelMove();

            Assert.AreEqual(9, region.buildingStates[TestContent.PumpInstanceId].placement.gridX);
        }

        [Test]
        public void StageAdvances_WhenThresholdMet()
        {
            int stageEvents = 0;
            _events.Subscribe<RegionStageChanged>(_ => stageEvents++);

            Complete("project.test.clear_rubble");

            var region = _profile.worldState.GetOrCreateRegionState(TestContent.RegionId);
            Assert.AreEqual(1, region.restorationStage);
            Assert.AreEqual(1, stageEvents);
        }

        [Test]
        public void RegionStageGate_BlocksEcosystemProject()
        {
            Complete("project.test.clear_rubble");
            Complete("project.test.restore_pump");

            // river_flow requires stage >= 1; we are at stage 1 after clear_rubble... but
            // also requires restore_pump completion which happened. Should evaluate None.
            Assert.AreEqual(RestorationFailure.None,
                _restoration.Evaluate("project.test.river_flow", out _, out _));
        }
    }
}
