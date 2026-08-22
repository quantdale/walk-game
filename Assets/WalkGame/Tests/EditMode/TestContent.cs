using System;
using System.Collections.Generic;
using NUnit.Framework;
using WalkGame.Activity;
using WalkGame.Building;
using WalkGame.Core;
using WalkGame.Gameplay;
using WalkGame.Persistence;

namespace WalkGame.Tests
{
    /// <summary>
    /// Shared deterministic content used across domain tests: a small region with
    /// two buildings, one producer, and a three-project restoration chain.
    /// </summary>
    internal static class TestContent
    {
        public const string RegionId = "region.test";
        public const string PumpInstanceId = "building.test.pump";
        public const string GroveInstanceId = "building.test.grove";
        public const string DamInstanceId = "building.test.dam";
        public const string PumpProducerId = "producer.test.pump";

        public static ContentCatalog Create()
        {
            var catalog = new ContentCatalog();

            var pump = new BuildingDefinition
            {
                definitionId = "building.pump_station",
                displayNameKey = "pump",
                footprint = new FootprintDefinition { widthCells = 2, depthCells = 2 },
                movableAfterRestore = true,
                maxUpgradeTier = 3,
                producerDefinitionId = PumpProducerId,
            };
            pump.upgradeDefinitions.Add(new UpgradeTierDefinition { tier = 1, vitalityCost = 100 });
            pump.upgradeDefinitions.Add(new UpgradeTierDefinition { tier = 2, vitalityCost = 200, productionMultiplier = 1.5 });
            catalog.Buildings.Add(pump);

            var grove = new BuildingDefinition
            {
                definitionId = "building.grove",
                displayNameKey = "grove",
                footprint = new FootprintDefinition { widthCells = 1, depthCells = 1 },
                movableAfterRestore = true,
                maxUpgradeTier = 1,
            };
            catalog.Buildings.Add(grove);

            var dam = new BuildingDefinition
            {
                definitionId = "building.dam",
                displayNameKey = "dam",
                footprint = new FootprintDefinition { widthCells = 3, depthCells = 1 },
                movableAfterRestore = false,
                maxUpgradeTier = 1,
            };
            catalog.Buildings.Add(dam);

            catalog.Producers.Add(new ProducerDefinition
            {
                producerId = PumpProducerId,
                resourceId = WellKnownIds.Resources.Water,
                baseRatePerHour = 10.0,
                storageCap = 1000,
                offlineCapHours = 8.0,
                tierMultipliers = { { 1, 1.0 }, { 2, 1.5 }, { 3, 2.25 } },
            });

            var clearRubble = new RestorationProjectDefinition
            {
                projectId = "project.test.clear_rubble",
                regionId = RegionId,
                category = ProjectCategory.Micro,
                vitalityCost = 50,
                rewardActions =
                {
                    RewardActionDefinition.Score("infrastructure", 5),
                },
            };

            var restorePump = new RestorationProjectDefinition
            {
                projectId = "project.test.restore_pump",
                regionId = RegionId,
                category = ProjectCategory.Building,
                vitalityCost = 150,
                prerequisiteProjectIds = { clearRubble.projectId },
                rewardActions =
                {
                    new RewardActionDefinition { kind = RewardActionKind.SetBuildingRestored, targetId = PumpInstanceId },
                    RewardActionDefinition.Score("infrastructure", 10),
                },
            };

            var riverFlow = new RestorationProjectDefinition
            {
                projectId = "project.test.river_flow",
                regionId = RegionId,
                category = ProjectCategory.Ecosystem,
                vitalityCost = 300,
                prerequisiteProjectIds = { restorePump.projectId },
                requiredRegionStage = 1,
                rewardActions =
                {
                    RewardActionDefinition.Score("ecology", 20),
                    new RewardActionDefinition { kind = RewardActionKind.SetEnvironmentFlag, targetId = WellKnownIds.EnvironmentFlags.RiverFlowing },
                },
            };

            catalog.Projects.AddRange(new[] { clearRubble, restorePump, riverFlow });

            catalog.Regions[RegionId] = new RegionDefinition
            {
                regionId = RegionId,
                displayNameKey = "Test Basin",
                placementOriginX = 0,
                placementOriginY = 0,
                placementWidthCells = 16,
                placementDepthCells = 16,
                defaultBuildingInstances =
                {
                    new DefaultBuildingInstanceDefinition
                    {
                        instanceId = PumpInstanceId,
                        buildingDefinitionId = pump.definitionId,
                        initialPlacement = new BuildingPlacement { gridX = 4, gridY = 4 },
                        producerId = PumpProducerId,
                    },
                    new DefaultBuildingInstanceDefinition
                    {
                        instanceId = GroveInstanceId,
                        buildingDefinitionId = grove.definitionId,
                        initialPlacement = new BuildingPlacement { gridX = 8, gridY = 8 },
                    },
                    new DefaultBuildingInstanceDefinition
                    {
                        instanceId = DamInstanceId,
                        buildingDefinitionId = dam.definitionId,
                        initialPlacement = new BuildingPlacement { gridX = 12, gridY = 2 },
                        fixedPlacement = true,
                    },
                },
                stageThresholds =
                {
                    new StageThresholdDefinition
                    {
                        stage = 1,
                        totalScoreRequired = 5,
                        requiredProjectIds = { clearRubble.projectId },
                    },
                },
                reservedAreas =
                {
                    new ReservedArea { originX = 14, originY = 0, widthCells = 2, depthCells = 16 },
                },
            };

            catalog.Milestones.Add(new MilestoneDefinition
            {
                milestoneId = "milestone.first_spark",
                lifetimeStepsRequired = 1000,
                vitalityReward = 25,
            });

            return catalog;
        }
    }

    /// <summary>Plain in-memory catalog implementation shared by tests.</summary>
    public sealed class ContentCatalog : IContentCatalog
    {
        public Dictionary<string, RegionDefinition> Regions { get; } = new Dictionary<string, RegionDefinition>();
        public List<BuildingDefinition> Buildings { get; } = new List<BuildingDefinition>();
        public List<ProducerDefinition> Producers { get; } = new List<ProducerDefinition>();
        public List<RestorationProjectDefinition> Projects { get; } = new List<RestorationProjectDefinition>();
        public List<MilestoneDefinition> Milestones { get; } = new List<MilestoneDefinition>();

        private readonly Dictionary<string, BuildingDefinition> _buildingsById = new Dictionary<string, BuildingDefinition>();
        private readonly Dictionary<string, ProducerDefinition> _producersById = new Dictionary<string, ProducerDefinition>();

        public void Index()
        {
            _buildingsById.Clear();
            foreach (var building in Buildings)
            {
                if (!string.IsNullOrEmpty(building?.definitionId))
                {
                    _buildingsById[building.definitionId] = building;
                }
            }

            _producersById.Clear();
            foreach (var producer in Producers)
            {
                if (!string.IsNullOrEmpty(producer?.producerId))
                {
                    _producersById[producer.producerId] = producer;
                }
            }
        }

        public RegionDefinition GetRegion(string regionId)
        {
            return Regions.TryGetValue(regionId, out var region) ? region : null;
        }

        public BuildingDefinition GetBuilding(string definitionId)
        {
            return _buildingsById.TryGetValue(definitionId, out var building) ? building : null;
        }

        public ProducerDefinition GetProducer(string producerId)
        {
            return _producersById.TryGetValue(producerId, out var producer) ? producer : null;
        }

        public IReadOnlyList<RestorationProjectDefinition> GetProjectsForRegion(string regionId)
        {
            return Projects.FindAll(p => p != null && p.regionId == regionId);
        }

        IReadOnlyList<MilestoneDefinition> IContentCatalog.GetMilestones()
        {
            return Milestones;
        }
    }
}
