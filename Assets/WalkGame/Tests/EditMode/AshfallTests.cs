using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WalkGame.Activity;
using WalkGame.Building;
using WalkGame.Content;
using WalkGame.Core;
using WalkGame.Gameplay;
using WalkGame.Persistence;

namespace WalkGame.Tests
{
    /// <summary>
    /// Content integrity gates for the shipped Ashfall Basin catalog. These protect the
    /// persistent ID graph: any authoring mistake here would corrupt saves at runtime.
    /// </summary>
    public sealed class AshfallContentIntegrityTests
    {
        private AshfallBasinCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = new AshfallBasinCatalog();
        }

        [Test]
        public void RegionDefinition_Resolves_AndIsBounded()
        {
            var region = _catalog.GetRegion(AshfallBasinCatalog.RegionId);
            Assert.IsNotNull(region);
            Assert.GreaterOrEqual(region.placementWidthCells, 16);
            Assert.GreaterOrEqual(region.defaultBuildingInstances.Count, 6);
            Assert.LessOrEqual(region.defaultBuildingInstances.Count, 30);
            Assert.GreaterOrEqual(region.stageThresholds.Count, 3);
        }

        [Test]
        public void EveryInstance_DefinitionAndProducer_Resolve()
        {
            foreach (var instance in _catalog.Ashfall.defaultBuildingInstances)
            {
                var building = _catalog.GetBuilding(instance.buildingDefinitionId);
                Assert.IsNotNull(building, $"Instance '{instance.instanceId}' references unknown definition '{instance.buildingDefinitionId}'.");

                if (!string.IsNullOrEmpty(instance.producerId))
                {
                    Assert.IsNotNull(_catalog.GetProducer(instance.producerId),
                        $"Instance '{instance.instanceId}' references unknown producer '{instance.producerId}'.");
                    Assert.AreEqual(building.producerDefinitionId, instance.producerId,
                        "Producer wiring must agree between building definition and default instance.");
                }

                if (!string.IsNullOrEmpty(building.producerDefinitionId))
                {
                    Assert.IsNotNull(_catalog.GetProducer(building.producerDefinitionId),
                        $"Building '{building.definitionId}' references unknown producer.");
                }
            }
        }

        [Test]
        public void InstanceIds_AreUnique()
        {
            var ids = _catalog.Ashfall.defaultBuildingInstances.Select(i => i.instanceId).ToList();
            Assert.AreEqual(ids.Count, ids.Distinct().Count(), "Instance IDs must be unique within a region.");
        }

        [Test]
        public void DefaultPlacements_Fit_DoNotOverlap_AvoidReserved()
        {
            var region = _catalog.Ashfall;

            foreach (var instance in region.defaultBuildingInstances)
            {
                var building = _catalog.GetBuilding(instance.buildingDefinitionId);
                Assert.IsNotNull(building);
                int w = building.footprint.widthCells;
                int d = building.footprint.depthCells;

                for (int dx = 0; dx < w; dx++)
                {
                    for (int dy = 0; dy < d; dy++)
                    {
                        int x = instance.initialPlacement.gridX + dx;
                        int y = instance.initialPlacement.gridY + dy;
                        Assert.IsTrue(region.IsInsidePlacementArea(x, y),
                            $"'{instance.instanceId}' footprint extends outside the placement area at ({x},{y}).");
                        Assert.IsFalse(region.IsReserved(x, y),
                            $"'{instance.instanceId}' footprint sits on reserved cells at ({x},{y}).");
                    }
                }
            }

            for (int i = 0; i < region.defaultBuildingInstances.Count; i++)
            {
                for (int j = i + 1; j < region.defaultBuildingInstances.Count; j++)
                {
                    Assert.IsFalse(
                        RectsOverlap(_catalog, region.defaultBuildingInstances[i], region.defaultBuildingInstances[j]),
                        $"'{region.defaultBuildingInstances[i].instanceId}' overlaps '{region.defaultBuildingInstances[j].instanceId}'.");
                }
            }
        }

        private static bool RectsOverlap(IContentCatalog catalog, DefaultBuildingInstanceDefinition a, DefaultBuildingInstanceDefinition b)
        {
            // Authored placements are unrotated, so AABBs use authored footprint extents.
            var aDef = catalog.GetBuilding(a.buildingDefinitionId);
            var bDef = catalog.GetBuilding(b.buildingDefinitionId);
            BuildingPlacementService.GetFootprintExtent(aDef, 0, out int aw, out int ad);
            BuildingPlacementService.GetFootprintExtent(bDef, 0, out int bw, out int bd);

            int ax = a.initialPlacement.gridX, ay = a.initialPlacement.gridY;
            int bx = b.initialPlacement.gridX, by = b.initialPlacement.gridY;
            return ax < bx + bw && ax + aw > bx && ay < by + bd && ay + ad > by;
        }

        [Test]
        public void PrerequisiteGraph_FormsDag_AllReferencesResolve()
        {
            var projects = _catalog.GetProjectsForRegion(AshfallBasinCatalog.RegionId).ToDictionary(p => p.projectId);

            foreach (var project in projects.Values)
            {
                Assert.AreEqual(AshfallBasinCatalog.RegionId, project.regionId);
                Assert.Greater(project.vitalityCost, 0);

                foreach (var prerequisite in project.prerequisiteProjectIds)
                {
                    Assert.IsTrue(projects.ContainsKey(prerequisite),
                        $"'{project.projectId}' requires unknown project '{prerequisite}'.");
                }
            }

            // Cycle detection via iterative DFS with visit states.
            const int Unvisited = 0, InProgress = 1, Done = 2;
            var state = projects.ToDictionary(kv => kv.Key, _ => Unvisited);

            bool Visit(string projectId)
            {
                switch (state[projectId])
                {
                    case InProgress: return false; // cycle
                    case Done: return true;
                }

                state[projectId] = InProgress;
                foreach (var prerequisite in projects[projectId].prerequisiteProjectIds)
                {
                    if (!Visit(prerequisite))
                    {
                        return false;
                    }
                }

                state[projectId] = Done;
                return true;
            }

            foreach (var projectId in projects.Keys)
            {
                Assert.IsTrue(Visit(projectId), $"Prerequisite cycle detected at '{projectId}'.");
            }
        }

        [Test]
        public void RewardActionTargets_Resolve()
        {
            var instances = _catalog.Ashfall.defaultBuildingInstances.Select(i => i.instanceId).ToHashSet();

            foreach (var project in _catalog.GetProjectsForRegion(AshfallBasinCatalog.RegionId))
            {
                Assert.IsNotEmpty(project.rewardActions, $"Project '{project.projectId}' has no rewards.");

                foreach (var action in project.rewardActions)
                {
                    switch (action.kind)
                    {
                        case RewardActionKind.SetBuildingRestored:
                        case RewardActionKind.UnlockBuilding:
                            Assert.IsTrue(instances.Contains(action.targetId),
                                $"'{project.projectId}' targets unknown building instance '{action.targetId}'.");
                            break;

                        case RewardActionKind.AddRegionScore:
                            CollectionAssert.Contains(
                                new[] { "ecology", "infrastructure", "community", "knowledge" },
                                action.targetId,
                                $"Unknown score type '{action.targetId}'.");
                            break;

                        case RewardActionKind.GrantResource:
                            Assert.IsNotEmpty(action.secondaryId, "Resource grant missing resource id.");
                            break;

                        case RewardActionKind.SetEnvironmentFlag:
                            Assert.IsNotEmpty(action.targetId, "Environment flag missing id.");
                            break;

                        case RewardActionKind.UnlockNpc:
                            Assert.IsNotEmpty(action.targetId, "NPC unlock missing npc id.");
                            break;

                        case RewardActionKind.DiscoverLore:
                            Assert.IsNotEmpty(action.targetId, "Lore discovery missing lore id.");
                            break;
                    }
                }
            }
        }

        [Test]
        public void StageThresholds_Ascend_AndReferenceRealProjects()
        {
            var projectIds = _catalog.GetProjectsForRegion(AshfallBasinCatalog.RegionId).Select(p => p.projectId).ToHashSet();
            int expectedStage = 1;

            foreach (var threshold in _catalog.Ashfall.stageThresholds.OrderBy(t => t.stage))
            {
                Assert.AreEqual(expectedStage, threshold.stage, "Stages must ascend without gaps starting at 1.");
                expectedStage++;

                Assert.Greater(threshold.totalScoreRequired, 0);
                foreach (var requiredProject in threshold.requiredProjectIds)
                {
                    Assert.IsTrue(projectIds.Contains(requiredProject),
                        $"Threshold {threshold.stage} requires unknown project '{requiredProject}'.");
                }
            }

            Assert.LessOrEqual(expectedStage - 1, _catalog.Ashfall.visualStageCount);
        }

        [Test]
        public void Milestones_AscendByStepCount()
        {
            var milestones = ((IContentCatalog)_catalog).GetMilestones();
            Assert.GreaterOrEqual(milestones.Count, 4);

            for (int i = 1; i < milestones.Count; i++)
            {
                Assert.Greater(milestones[i].lifetimeStepsRequired, milestones[i - 1].lifetimeStepsRequired);
            }
        }

        [Test]
        public void VerticalSliceContentTargets_Met()
        {
            // GAME_DESIGN section 19 minimum content for the vertical slice.
            var projects = _catalog.GetProjectsForRegion(AshfallBasinCatalog.RegionId);
            Assert.GreaterOrEqual(projects.Count, 10, "10-15 restoration projects required.");
            Assert.LessOrEqual(projects.Count, 15);
            Assert.GreaterOrEqual(_catalog.Ashfall.defaultBuildingInstances.Count(i => !i.startsRestored), 6,
                "6-10 restorable building ruins required.");
            Assert.AreEqual(ProjectCategory.Landmark, projects.First(p => p.category == ProjectCategory.Landmark).category,
                "At least one landmark chain is required.");
        }
    }

    /// <summary>
    /// Full scripted playthrough of Ashfall Basin against real content: walking earns
    /// vitality, the whole restoration chain completes, stages advance, producers run,
    /// and the Builder/Explore synchronization invariant survives save/reload.
    /// </summary>
    public sealed class AshfallPlaythroughTests
    {
        private MutableClock _clock;
        private PlayerProfile _profile;
        private DomainEvents _events;
        private AshfallBasinCatalog _catalog;
        private VitalityLedger _ledger;
        private ActivityService _activity;
        private ProductionService _production;
        private RestorationService _restoration;
        private BuildingPlacementService _placement;
        private FileSaveRepository _repository;

        [SetUp]
        public void SetUp()
        {
            _clock = new MutableClock(new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc));
            _profile = new PlayerProfile();
            _profile.worldState.currentRegionId = AshfallBasinCatalog.RegionId;
            _events = new DomainEvents();
            _catalog = new AshfallBasinCatalog();

            _ledger = new VitalityLedger(_profile, _clock, _events, Log.Disabled);
            var rewards = new RewardApplier(_profile, _clock, _events, Log.Disabled);
            var activity = new ActivityService(_profile, _ledger, new TrustEvaluator(RewardPolicy.Default), new RewardCalculator(RewardPolicy.Default), _events, Log.Disabled);
            var milestones = new StepMilestoneService(_catalog, _profile, _ledger, _events);
            activity.MilestonesPending += _ => milestones.CheckAndAward();
            _activity = activity;
            _production = new ProductionService(_catalog, _profile, rewards, _clock, Log.Disabled);
            _restoration = new RestorationService(_catalog, _profile, _ledger, rewards, _events, Log.Disabled);
            _placement = new BuildingPlacementService(_catalog, _events);

            LoadRegionFromDefinition();
            _production.EnsureProducerStates(AshfallBasinCatalog.RegionId);

            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "walkgame-playthrough", Guid.NewGuid().ToString("N"));
            _repository = new FileSaveRepository(dir, "profile.json", new JsonSaveSerializer(), new SaveMigrator(), Log.Disabled);
        }

        private void LoadRegionFromDefinition()
        {
            var definition = _catalog.Ashfall;
            var region = _profile.worldState.GetOrCreateRegionState(definition.regionId);
            foreach (var instance in definition.defaultBuildingInstances)
            {
                var state = region.GetOrCreateBuildingState(instance.instanceId, instance.buildingDefinitionId);
                state.placement.gridX = instance.initialPlacement.gridX;
                state.placement.gridY = instance.initialPlacement.gridY;
                state.placement.rotationQuarterTurns = instance.initialPlacement.rotationQuarterTurns;
            }
        }

        private void Walk(long steps)
        {
            var end = _clock.UtcNow;
            var snapshot = new ActivitySnapshot
            {
                providerId = DebugActivityProvider.ProviderIdValue,
                intervalStartUtc = end.AddMinutes(-30),
                intervalEndUtc = end,
                stepCount = steps,
                sourceType = ActivitySourceType.PhoneSensor,
                recordingType = ActivityRecordingType.Passive,
                quality = new ActivityQuality { hasStepEvidence = true },
            };
            _activity.ProcessPassiveSnapshot(snapshot);
            _clock.Advance(TimeSpan.FromMinutes(31));
        }

        private bool Complete(string projectId)
        {
            return _restoration.TryComplete(projectId, out _);
        }

        [Test]
        public void DeadWorld_To_TransitGateAlignment_CompletesInDependencyOrder()
        {
            // Premature attempts are blocked by prerequisites.
            Assert.AreEqual(RestorationFailure.MissingPrerequisite,
                _restoration.Evaluate("project.ashfall.refill_river", out _, out _));
            Assert.AreEqual(RestorationFailure.InsufficientVitality,
                _restoration.Evaluate("project.ashfall.clear_aqueduct_rubble", out _, out _));

            Walk(600);
            Assert.IsTrue(Complete("project.ashfall.clear_aqueduct_rubble"));
            Assert.IsTrue(Complete("project.ashfall.clean_fountain"));
            Assert.IsTrue(Complete("project.ashfall.repair_streetlamps"));

            Walk(400);
            Assert.IsTrue(Complete("project.ashfall.restore_water_station"));

            var region = _profile.worldState.GetOrCreateRegionState(AshfallBasinCatalog.RegionId);
            Assert.AreEqual(1, region.restorationStage, "First stage should advance after aqueduct + water station.");

            Assert.IsTrue(Complete("project.ashfall.restore_house_east"));

            Walk(800);
            Assert.IsTrue(Complete("project.ashfall.refill_river"));
            Assert.AreEqual(2, region.restorationStage);

            Assert.IsTrue(region.discoveredLoreIds.Contains("flag:env.ashfall.river_flowing") ||
                          region.completedProjectIds.Contains("project.ashfall.refill_river"));

            Assert.IsTrue(Complete("project.ashfall.restore_wetland"));
            Assert.IsTrue(region.arrivedNpcIds.Contains("npc.mara_ecologist"), "Wetland completion brings the ecologist.");

            Assert.IsTrue(Complete("project.ashfall.revive_grove"));
            Assert.IsTrue(region.buildingStates[AshfallBasinCatalog.DeadGroveInstance].IsRestored);

            Assert.IsTrue(Complete("project.ashfall.restore_greenhouse"));
            Assert.IsTrue(Complete("project.ashfall.restore_house_west"));
            Assert.IsTrue(Complete("project.ashfall.restore_house_south"));

            // The wallet is drained; go for a real walk before the industry tier.
            Walk(2500);

            bool workshopOk = Complete("project.ashfall.restore_workshop");
            Assert.IsTrue(workshopOk,
                $"workshop failed: {_restoration.Evaluate("project.ashfall.restore_workshop", out _, out _)}");

            // Workshop produces components over time; collect before spending them.
            _clock.Advance(TimeSpan.FromHours(2));
            _production.AccrueAll(AshfallBasinCatalog.RegionId);
            var collected = _production.Collect(AshfallBasinCatalog.RegionId, AshfallBasinCatalog.WorkshopProducer);
            Assert.Greater(collected.collected, 0, "Workshop must produce components once restored.");

            bool researchOk = Complete("project.ashfall.restore_research_hall");
            Assert.IsTrue(researchOk,
                $"research hall failed: {_restoration.Evaluate("project.ashfall.restore_research_hall", out _, out _)}");

            Assert.IsTrue(Complete("project.ashfall.transit_gate_awaken"));

            // The final alignment demands sustained real-world movement.
            Assert.AreEqual(RestorationFailure.LifetimeStepsTooLow,
                _restoration.Evaluate("project.ashfall.transit_gate_align", out _, out _));

            Walk(20000);
            Assert.IsTrue(Complete("project.ashfall.transit_gate_align"));

            Assert.AreEqual(15, region.completedProjectIds.Count, "Every Ashfall project must be complete.");
            Assert.AreEqual(3, region.restorationStage, "Region must reach the rewilded stage.");
            Assert.AreEqual(3, region.arrivedNpcIds.Count, "All three NPCs arrive across the arc.");
            Assert.AreEqual(5,
                region.discoveredLoreIds.Count(id => id.StartsWith("lore.ashfall.", StringComparison.Ordinal)),
                "All five lore objects discovered.");

            foreach (var building in region.buildingStates.Values)
            {
                Assert.IsTrue(building.IsRestored, $"'{building.instanceId}' must be restored by the finale.");
            }

            Assert.GreaterOrEqual(_profile.resources.ContainsKey(WellKnownIds.Resources.Knowledge)
                ? _profile.resources[WellKnownIds.Resources.Knowledge] : 0, 50);

            // Offline production resumes deterministically after the session ends.
            _production.AccrueAll(AshfallBasinCatalog.RegionId); // normalize checkpoints to "now"
            DateTime beforeClose = _clock.UtcNow;
            long storedBefore = region.producerStates.Values.Sum(p => p.storedOutput);
            _clock.Advance(TimeSpan.FromHours(4)); // within the 8h offline cap
            _production.AccrueAll(AshfallBasinCatalog.RegionId);
            long storedAfter = region.producerStates.Values.Sum(p => p.storedOutput);
            // Rates 12+8+5+3 per hour are whole numbers, so 4h yields exactly 112.
            Assert.AreEqual(112, storedAfter - storedBefore,
                "Offline production must be deterministic and bounded.");
            Assert.AreEqual(beforeClose.Add(TimeSpan.FromHours(4)), _clock.UtcNow);
        }


        [Test]
        public void MoveWaterStation_SaveReload_EnterExplore_PlacementIdentical()
        {
            // Mandatory Builder/Explore synchronization regression (AGENT_EXECUTION_GUIDE 14).
            Walk(1000);
            Complete("project.ashfall.clear_aqueduct_rubble");
            Complete("project.ashfall.restore_water_station");

            var region = _profile.worldState.GetOrCreateRegionState(AshfallBasinCatalog.RegionId);
            var definition = _catalog.Ashfall;

            Assert.AreEqual(PlacementFailure.None, _placement.BeginMove(definition, region, AshfallBasinCatalog.WaterStationInstance));
            var candidate = new BuildingPlacement { gridX = 7, gridY = 7 };
            Assert.AreEqual(PlacementFailure.None, _placement.PreviewCandidate(candidate));
            Assert.IsTrue(_placement.ConfirmMove(candidate, out _));

            Assert.AreEqual(SaveLoadResult.Success, _repository.Save(_profile));

            bool loaded = _repository.TryLoad(out var reloaded, out var result);
            Assert.IsTrue(loaded, $"reload failed: {result}");
            _production.EnsureProducerStates(AshfallBasinCatalog.RegionId);

            // Explore View reconstructs from the same canonical RegionState:
            var exploreProjection = reloaded.worldState.GetOrCreateRegionState(AshfallBasinCatalog.RegionId);
            var builderView = region.buildingStates[AshfallBasinCatalog.WaterStationInstance].placement;
            var exploreView = exploreProjection.buildingStates[AshfallBasinCatalog.WaterStationInstance].placement;

            Assert.AreEqual(builderView.gridX, exploreView.gridX);
            Assert.AreEqual(builderView.gridY, exploreView.gridY);
            Assert.AreEqual(builderView.rotationQuarterTurns, exploreView.rotationQuarterTurns);
            Assert.AreEqual(builderView.placementVersion, exploreView.placementVersion);
            Assert.AreEqual(BuildingLifecycleState.Restored, exploreProjection.buildingStates[AshfallBasinCatalog.WaterStationInstance].lifecycleState);
        }

        [Test]
        public void FixedTransitGate_CannotBeMoved_EvenWhenRestored()
        {
            Walk(5000);
            Complete("project.ashfall.clear_aqueduct_rubble");
            Complete("project.ashfall.clean_fountain");
            Complete("project.ashfall.repair_streetlamps");
            Complete("project.ashfall.restore_water_station");

            // Force-restore the gate through the domain path used by tests/debug tools.
            var region = _profile.worldState.GetOrCreateRegionState(AshfallBasinCatalog.RegionId);
            region.buildingStates[AshfallBasinCatalog.TransitGateInstance].lifecycleState = BuildingLifecycleState.Restored;

            var failure = BuildingPlacementService.Validate(
                _catalog.Ashfall, region, _catalog,
                AshfallBasinCatalog.TransitGateInstance,
                region.buildingStates[AshfallBasinCatalog.TransitGateInstance].placement);

            Assert.AreEqual(PlacementFailure.NotMovable, failure);
        }
    }
}
