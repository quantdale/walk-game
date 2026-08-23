using System;
using System.Collections.Generic;
using WalkGame.Core;

namespace WalkGame.Content
{
    /// <summary>
    /// Ashfall Basin - the MVP vertical-slice region (GAME_DESIGN section 19).
    /// Content-as-code keeps IDs, chains and footprints deterministically testable;
    /// a ScriptableObject surface can wrap this catalog later without changing
    /// persistent IDs (see docs/adr/0002-content-as-code.md).
    ///
    /// Persistent IDs are API: never rename shipped IDs without a migration.
    /// </summary>
    public sealed class AshfallBasinCatalog : IContentCatalog
    {
        public const string RegionId = WellKnownIds.StartingRegionId;

        // Building definition ids.
        public const string WaterStationDefinition = "building.water_station";
        public const string GreenhouseDefinition = "building.greenhouse.small";
        public const string WorkshopDefinition = "building.workshop.small";
        public const string ResearchHallDefinition = "building.research_hall";
        public const string HouseEastDefinition = "building.house.east";
        public const string HouseWestDefinition = "building.house.west";
        public const string HouseSouthDefinition = "building.house.south";
        public const string DeadGroveDefinition = "building.grove.dead";
        public const string TransitGateDefinition = "landmark.transit_gate";

        // Building instance ids (stable forever once shipped).
        public const string WaterStationInstance = "inst.ashfall.water_station";
        public const string GreenhouseInstance = "inst.ashfall.greenhouse";
        public const string WorkshopInstance = "inst.ashfall.workshop";
        public const string ResearchHallInstance = "inst.ashfall.research_hall";
        public const string HouseEastInstance = "inst.ashfall.house_east";
        public const string HouseWestInstance = "inst.ashfall.house_west";
        public const string HouseSouthInstance = "inst.ashfall.house_south";
        public const string DeadGroveInstance = "inst.ashfall.grove";
        public const string TransitGateInstance = "inst.ashfall.transit_gate";

        // Producer ids.
        public const string WaterStationProducer = "producer.ashfall.water_station";
        public const string GreenhouseProducer = "producer.ashfall.greenhouse";
        public const string WorkshopProducer = "producer.ashfall.workshop";
        public const string ResearchHallProducer = "producer.ashfall.research_hall";

        private readonly List<BuildingDefinition> _buildings = new List<BuildingDefinition>();
        private readonly Dictionary<string, BuildingDefinition> _buildingsById = new Dictionary<string, BuildingDefinition>();
        private readonly Dictionary<string, ProducerDefinition> _producersById = new Dictionary<string, ProducerDefinition>();
        private readonly List<RestorationProjectDefinition> _projects = new List<RestorationProjectDefinition>();
        private readonly List<MilestoneDefinition> _milestones = new List<MilestoneDefinition>();

        public RegionDefinition Ashfall { get; }

        public IReadOnlyList<ProducerDefinition> Producers => _producerList;
        private readonly Dictionary<string, ProducerDefinition> _producers = new Dictionary<string, ProducerDefinition>();
        private readonly List<ProducerDefinition> _producerList = new List<ProducerDefinition>();

        public AshfallBasinCatalog()
        {
            Ashfall = BuildRegion();
            BuildMilestones();
            Index();
        }

        private void BuildMilestones()
        {
            // Celebratory, permanent, modest economy impact (GAME_DESIGN section 13).
            _milestones.AddRange(new[]
            {
                new MilestoneDefinition
                {
                    milestoneId = "milestone.first_spark",
                    lifetimeStepsRequired = 1000,
                    vitalityReward = 25,
                    titleKey = "milestone.first_spark.title",
                },
                new MilestoneDefinition
                {
                    milestoneId = "milestone.ten_thousand",
                    lifetimeStepsRequired = 10000,
                    vitalityReward = 100,
                    titleKey = "milestone.ten_thousand.title",
                },
                new MilestoneDefinition
                {
                    milestoneId = "milestone.fifty_thousand",
                    lifetimeStepsRequired = 50000,
                    vitalityReward = 250,
                    titleKey = "milestone.fifty_thousand.title",
                },
                new MilestoneDefinition
                {
                    milestoneId = "milestone.hundred_thousand",
                    lifetimeStepsRequired = 100000,
                    vitalityReward = 400,
                    titleKey = "milestone.hundred_thousand.title",
                },
            });
        }

        private static ProducerDefinition Producer(string id, string resourceId, double ratePerHour, long cap, params (int tier, double mult)[] tiers)
        {
            var definition = new ProducerDefinition
            {
                producerId = id,
                resourceId = resourceId,
                baseRatePerHour = ratePerHour,
                storageCap = cap,
                offlineCapHours = 8.0,
            };
            foreach (var (tier, mult) in tiers)
            {
                definition.tierMultipliers[tier] = mult;
            }

            return definition;
        }

        private RegionDefinition BuildRegion()
        {
            _buildings.Add(new BuildingDefinition
            {
                definitionId = WaterStationDefinition,
                displayNameKey = "building.water_station.name",
                footprint = new FootprintDefinition { widthCells = 2, depthCells = 2 },
                movableAfterRestore = true,
                maxUpgradeTier = 3,
                producerDefinitionId = WaterStationProducer,
                upgradeDefinitions =
                {
                    new UpgradeTierDefinition { tier = 2, vitalityCost = 250 },
                    new UpgradeTierDefinition { tier = 3, vitalityCost = 600 },
                },
            });

            _buildings.Add(new BuildingDefinition
            {
                definitionId = GreenhouseDefinition,
                displayNameKey = "building.greenhouse.name",
                footprint = new FootprintDefinition { widthCells = 3, depthCells = 2 },
                movableAfterRestore = true,
                maxUpgradeTier = 3,
                producerDefinitionId = GreenhouseProducer,
                upgradeDefinitions =
                {
                    new UpgradeTierDefinition { tier = 2, vitalityCost = 280 },
                    new UpgradeTierDefinition { tier = 3, vitalityCost = 650 },
                },
            });

            _buildings.Add(new BuildingDefinition
            {
                definitionId = WorkshopDefinition,
                displayNameKey = "building.workshop.name",
                footprint = new FootprintDefinition { widthCells = 2, depthCells = 2 },
                movableAfterRestore = true,
                maxUpgradeTier = 2,
                producerDefinitionId = WorkshopProducer,
                upgradeDefinitions =
                {
                    new UpgradeTierDefinition
                    {
                        tier = 2,
                        vitalityCost = 320,
                        resourceCosts = { { WellKnownIds.Resources.Salvage, 20 } },
                    },
                },
            });

            _buildings.Add(new BuildingDefinition
            {
                definitionId = ResearchHallDefinition,
                displayNameKey = "building.research_hall.name",
                footprint = new FootprintDefinition { widthCells = 2, depthCells = 3 },
                movableAfterRestore = true,
                maxUpgradeTier = 2,
                producerDefinitionId = ResearchHallProducer,
                upgradeDefinitions =
                {
                    new UpgradeTierDefinition
                    {
                        tier = 2,
                        vitalityCost = 500,
                        resourceCosts = { { WellKnownIds.Resources.Components, 15 } },
                    },
                },
            });

            foreach (var house in new[] { HouseEastDefinition, HouseWestDefinition, HouseSouthDefinition })
            {
                _buildings.Add(new BuildingDefinition
                {
                    definitionId = house,
                    displayNameKey = house + ".name",
                    footprint = new FootprintDefinition { widthCells = 2, depthCells = 2 },
                    movableAfterRestore = true,
                    maxUpgradeTier = 1,
                });
            }

            _buildings.Add(new BuildingDefinition
            {
                definitionId = DeadGroveDefinition,
                displayNameKey = "building.grove.name",
                footprint = new FootprintDefinition { widthCells = 3, depthCells = 3 },
                movableAfterRestore = true,
                maxUpgradeTier = 1,
            });

            _buildings.Add(new BuildingDefinition
            {
                definitionId = TransitGateDefinition,
                displayNameKey = "landmark.transit_gate.name",
                footprint = new FootprintDefinition { widthCells = 2, depthCells = 4 },
                // Landmarks integrated with terrain stay fixed (WORLD_BUILDING_SYSTEM 8).
                movableAfterRestore = false,
                maxUpgradeTier = 1,
            });

            var region = new RegionDefinition
            {
                regionId = RegionId,
                displayNameKey = "region.ashfall.name",
                sceneReference = "Assets/WalkGame/Content/Regions/AshfallBasin.unity",
                placementOriginX = 0,
                placementOriginY = 0,
                placementWidthCells = 32,
                placementDepthCells = 32,
                exploreSpawnGridX = 16,
                exploreSpawnGridY = 6,
                visualStageCount = 4,
            };

            // The dry riverbed crosses the region and stays unbuildable; the spawn plaza is reserved.
            region.reservedAreas.Add(new ReservedArea { originX = 14, originY = 0, widthCells = 4, depthCells = 32 });
            region.reservedAreas.Add(new ReservedArea { originX = 12, originY = 4, widthCells = 8, depthCells = 4 });

            region.defaultBuildingInstances.AddRange(new[]
            {
                new DefaultBuildingInstanceDefinition
                {
                    instanceId = WaterStationInstance,
                    buildingDefinitionId = WaterStationDefinition,
                    initialPlacement = new BuildingPlacement { gridX = 5, gridY = 10 },
                    producerId = WaterStationProducer,
                },
                new DefaultBuildingInstanceDefinition
                {
                    instanceId = GreenhouseInstance,
                    buildingDefinitionId = GreenhouseDefinition,
                    initialPlacement = new BuildingPlacement { gridX = 20, gridY = 22 },
                    producerId = GreenhouseProducer,
                },
                new DefaultBuildingInstanceDefinition
                {
                    instanceId = WorkshopInstance,
                    buildingDefinitionId = WorkshopDefinition,
                    initialPlacement = new BuildingPlacement { gridX = 9, gridY = 20 },
                    producerId = WorkshopProducer,
                },
                new DefaultBuildingInstanceDefinition
                {
                    instanceId = ResearchHallInstance,
                    buildingDefinitionId = ResearchHallDefinition,
                    initialPlacement = new BuildingPlacement { gridX = 24, gridY = 12 },
                    producerId = ResearchHallProducer,
                },
                new DefaultBuildingInstanceDefinition
                {
                    instanceId = HouseEastInstance,
                    buildingDefinitionId = HouseEastDefinition,
                    initialPlacement = new BuildingPlacement { gridX = 21, gridY = 7 },
                },
                new DefaultBuildingInstanceDefinition
                {
                    instanceId = HouseWestInstance,
                    buildingDefinitionId = HouseWestDefinition,
                    initialPlacement = new BuildingPlacement { gridX = 4, gridY = 24 },
                },
                new DefaultBuildingInstanceDefinition
                {
                    instanceId = HouseSouthInstance,
                    buildingDefinitionId = HouseSouthDefinition,
                    initialPlacement = new BuildingPlacement { gridX = 27, gridY = 26 },
                },
                new DefaultBuildingInstanceDefinition
                {
                    instanceId = DeadGroveInstance,
                    buildingDefinitionId = DeadGroveDefinition,
                    initialPlacement = new BuildingPlacement { gridX = 25, gridY = 19 },
                },
                new DefaultBuildingInstanceDefinition
                {
                    instanceId = TransitGateInstance,
                    buildingDefinitionId = TransitGateDefinition,
                    initialPlacement = new BuildingPlacement { gridX = 19, gridY = 26 },
                    fixedPlacement = true,
                },
            });

            region.stageThresholds.AddRange(new[]
            {
                new StageThresholdDefinition
                {
                    stage = 1,
                    totalScoreRequired = 12,
                    requiredProjectIds = { "project.ashfall.clear_aqueduct_rubble" },
                    visualProfileId = "visual.ashfall.first_growth",
                },
                new StageThresholdDefinition
                {
                    stage = 2,
                    totalScoreRequired = 40,
                    requiredProjectIds = { "project.ashfall.refill_river" },
                    visualProfileId = "visual.ashfall.recovering",
                },
                new StageThresholdDefinition
                {
                    stage = 3,
                    totalScoreRequired = 80,
                    requiredProjectIds = { "project.ashfall.revive_grove", "project.ashfall.restore_wetland" },
                    visualProfileId = "visual.ashfall.rewilded",
                },
            });

            region.npcs.AddRange(new[]
            {
                new NpcDefinition
                {
                    npcId = "npc.mara_ecologist",
                    displayNameKey = "Mara, Basin Ecologist",
                    roleKey = "Wetland keeper",
                    arrivalPrerequisiteProjectId = "project.ashfall.restore_wetland",
                    spawnAnchorId = "anchor.wetland",
                    dialogueKey = "The river remembers its way. Keep moving, and the basin will remember life.",
                },
                new NpcDefinition
                {
                    npcId = "npc.ivo_historian",
                    displayNameKey = "Ivo, Gate Historian",
                    roleKey = "Transit historian",
                    arrivalPrerequisiteProjectId = "project.ashfall.transit_gate_awaken",
                    spawnAnchorId = "anchor.transit_gate",
                    dialogueKey = "The gate was built to carry restoration farther than any one pair of feet.",
                },
                new NpcDefinition
                {
                    npcId = "npc.bren_builder",
                    displayNameKey = "Bren, Basin Builder",
                    roleKey = "Settlement builder",
                    arrivalPrerequisiteProjectId = "project.ashfall.transit_gate_align",
                    spawnAnchorId = "anchor.settlement",
                    dialogueKey = "A repaired place is useful. A place arranged with care becomes home.",
                },
            });

            region.loreObjects.AddRange(new[]
            {
                new LoreDefinition
                {
                    loreId = "lore.ashfall.aqueduct_plaque",
                    titleKey = "Aqueduct plaque",
                    bodyKey = "Water was once measured in seasons here. The stone still points uphill.",
                    prerequisiteProjectId = "project.ashfall.clear_aqueduct_rubble",
                    anchorId = "anchor.aqueduct",
                },
                new LoreDefinition
                {
                    loreId = "lore.ashfall.pump_logbook",
                    titleKey = "Pump station logbook",
                    bodyKey = "Day 1: the pumps answer. Day 12: a green line has appeared along the river.",
                    prerequisiteProjectId = "project.ashfall.restore_water_station",
                    anchorId = "anchor.water_station",
                },
                new LoreDefinition
                {
                    loreId = "lore.ashfall.riverside_letters",
                    titleKey = "Riverside letters",
                    bodyKey = "We left the gate open for whoever could teach the basin to breathe again.",
                    prerequisiteProjectId = "project.ashfall.refill_river",
                    anchorId = "anchor.riverside",
                },
                new LoreDefinition
                {
                    loreId = "lore.ashfall.greenhouse_seeds",
                    titleKey = "Seed archive",
                    bodyKey = "A small tin of seeds, labelled for the first rain after ashfall.",
                    prerequisiteProjectId = "project.ashfall.restore_greenhouse",
                    anchorId = "anchor.greenhouse",
                },
                new LoreDefinition
                {
                    loreId = "lore.ashfall.gate_inscription",
                    titleKey = "Transit gate inscription",
                    bodyKey = "No road is finished when it reaches a wall. Restore the way, then choose where it leads.",
                    prerequisiteProjectId = "project.ashfall.transit_gate_awaken",
                    anchorId = "anchor.transit_gate",
                },
            });

            BuildProjects(region);
            BuildProducers();

            return region;
        }

        private void BuildProjects(RegionDefinition region)
        {
            // --- Micro restorations: frequent feedback (GAME_DESIGN section 5). ---
            _projects.Add(new RestorationProjectDefinition
            {
                projectId = "project.ashfall.clear_aqueduct_rubble",
                regionId = RegionId,
                category = ProjectCategory.Micro,
                vitalityCost = 40,
                rewardActions =
                {
                    RewardActionDefinition.Score("infrastructure", 4),
                    // Clearing collapsed aqueduct debris yields usable salvage for the
                    // water station and workshop projects downstream.
                    new RewardActionDefinition { kind = RewardActionKind.GrantResource, secondaryId = WellKnownIds.Resources.Salvage, amount = 20 },
                    new RewardActionDefinition { kind = RewardActionKind.DiscoverLore, targetId = "lore.ashfall.aqueduct_plaque" },
                },
                titleKey = "project.ashfall.clear_aqueduct_rubble.title",
                descriptionKey = "project.ashfall.clear_aqueduct_rubble.desc",
            });

            _projects.Add(new RestorationProjectDefinition
            {
                projectId = "project.ashfall.clean_fountain",
                regionId = RegionId,
                category = ProjectCategory.Micro,
                vitalityCost = 30,
                rewardActions = { RewardActionDefinition.Score("community", 3) },
                titleKey = "project.ashfall.clean_fountain.title",
                descriptionKey = "project.ashfall.clean_fountain.desc",
            });

            _projects.Add(new RestorationProjectDefinition
            {
                projectId = "project.ashfall.repair_streetlamps",
                regionId = RegionId,
                category = ProjectCategory.Micro,
                vitalityCost = 35,
                rewardActions = { RewardActionDefinition.Score("community", 3) },
                titleKey = "project.ashfall.repair_streetlamps.title",
                descriptionKey = "project.ashfall.repair_streetlamps.desc",
            });

            // --- Building restorations. ---
            _projects.Add(new RestorationProjectDefinition
            {
                projectId = "project.ashfall.restore_water_station",
                regionId = RegionId,
                category = ProjectCategory.Building,
                vitalityCost = 150,
                prerequisiteProjectIds = { "project.ashfall.clear_aqueduct_rubble" },
                resourceCosts = { { WellKnownIds.Resources.Salvage, 5 } },
                rewardActions =
                {
                    new RewardActionDefinition { kind = RewardActionKind.SetBuildingRestored, targetId = WaterStationInstance },
                    RewardActionDefinition.Score("infrastructure", 10),
                    new RewardActionDefinition { kind = RewardActionKind.DiscoverLore, targetId = "lore.ashfall.pump_logbook" },
                },
                titleKey = "project.ashfall.restore_water_station.title",
                descriptionKey = "project.ashfall.restore_water_station.desc",
            });

            _projects.Add(new RestorationProjectDefinition
            {
                projectId = "project.ashfall.restore_greenhouse",
                regionId = RegionId,
                category = ProjectCategory.Building,
                vitalityCost = 160,
                prerequisiteProjectIds = { "project.ashfall.refill_river" },
                rewardActions =
                {
                    new RewardActionDefinition { kind = RewardActionKind.SetBuildingRestored, targetId = GreenhouseInstance },
                    RewardActionDefinition.Score("ecology", 8),
                    new RewardActionDefinition { kind = RewardActionKind.DiscoverLore, targetId = "lore.ashfall.greenhouse_seeds" },
                },
                titleKey = "project.ashfall.restore_greenhouse.title",
                descriptionKey = "project.ashfall.restore_greenhouse.desc",
            });

            _projects.Add(new RestorationProjectDefinition
            {
                projectId = "project.ashfall.restore_workshop",
                regionId = RegionId,
                category = ProjectCategory.Building,
                vitalityCost = 170,
                prerequisiteProjectIds = { "project.ashfall.clean_fountain" },
                resourceCosts = { { WellKnownIds.Resources.Salvage, 10 } },
                rewardActions =
                {
                    new RewardActionDefinition { kind = RewardActionKind.SetBuildingRestored, targetId = WorkshopInstance },
                    RewardActionDefinition.Score("infrastructure", 10),
                },
                titleKey = "project.ashfall.restore_workshop.title",
                descriptionKey = "project.ashfall.restore_workshop.desc",
            });

            _projects.Add(new RestorationProjectDefinition
            {
                projectId = "project.ashfall.restore_research_hall",
                regionId = RegionId,
                category = ProjectCategory.Building,
                vitalityCost = 220,
                prerequisiteProjectIds = { "project.ashfall.restore_workshop" },
                resourceCosts = { { WellKnownIds.Resources.Components, 5 } },
                rewardActions =
                {
                    new RewardActionDefinition { kind = RewardActionKind.SetBuildingRestored, targetId = ResearchHallInstance },
                    RewardActionDefinition.Score("knowledge", 12),
                },
                titleKey = "project.ashfall.restore_research_hall.title",
                descriptionKey = "project.ashfall.restore_research_hall.desc",
            });

            _projects.Add(new RestorationProjectDefinition
            {
                projectId = "project.ashfall.restore_house_east",
                regionId = RegionId,
                category = ProjectCategory.Building,
                vitalityCost = 120,
                prerequisiteProjectIds = { "project.ashfall.clear_aqueduct_rubble" },
                rewardActions =
                {
                    new RewardActionDefinition { kind = RewardActionKind.SetBuildingRestored, targetId = HouseEastInstance },
                    RewardActionDefinition.Score("community", 8),
                },
                titleKey = "project.ashfall.restore_house_east.title",
                descriptionKey = "project.ashfall.restore_house_east.desc",
            });

            _projects.Add(new RestorationProjectDefinition
            {
                projectId = "project.ashfall.restore_house_west",
                regionId = RegionId,
                category = ProjectCategory.Building,
                vitalityCost = 120,
                prerequisiteProjectIds = { "project.ashfall.refill_river" },
                rewardActions =
                {
                    new RewardActionDefinition { kind = RewardActionKind.SetBuildingRestored, targetId = HouseWestInstance },
                    RewardActionDefinition.Score("community", 8),
                },
                titleKey = "project.ashfall.restore_house_west.title",
                descriptionKey = "project.ashfall.restore_house_west.desc",
            });

            _projects.Add(new RestorationProjectDefinition
            {
                projectId = "project.ashfall.restore_house_south",
                regionId = RegionId,
                category = ProjectCategory.Building,
                vitalityCost = 120,
                prerequisiteProjectIds = { "project.ashfall.revive_grove" },
                rewardActions =
                {
                    new RewardActionDefinition { kind = RewardActionKind.SetBuildingRestored, targetId = HouseSouthInstance },
                    RewardActionDefinition.Score("community", 8),
                },
                titleKey = "project.ashfall.restore_house_south.title",
                descriptionKey = "project.ashfall.restore_house_south.desc",
            });

            // --- Ecosystem chain: why one restoration enables the next. ---
            _projects.Add(new RestorationProjectDefinition
            {
                projectId = "project.ashfall.refill_river",
                regionId = RegionId,
                category = ProjectCategory.Ecosystem,
                vitalityCost = 300,
                prerequisiteProjectIds = { "project.ashfall.restore_water_station" },
                requiredRegionStage = 1,
                rewardActions =
                {
                    new RewardActionDefinition { kind = RewardActionKind.SetEnvironmentFlag, targetId = WellKnownIds.EnvironmentFlags.RiverFlowing },
                    RewardActionDefinition.Score("ecology", 20),
                    new RewardActionDefinition { kind = RewardActionKind.DiscoverLore, targetId = "lore.ashfall.riverside_letters" },
                },
                titleKey = "project.ashfall.refill_river.title",
                descriptionKey = "project.ashfall.refill_river.desc",
            });

            _projects.Add(new RestorationProjectDefinition
            {
                projectId = "project.ashfall.restore_wetland",
                regionId = RegionId,
                category = ProjectCategory.Ecosystem,
                vitalityCost = 260,
                prerequisiteProjectIds = { "project.ashfall.refill_river" },
                rewardActions =
                {
                    new RewardActionDefinition { kind = RewardActionKind.SetEnvironmentFlag, targetId = WellKnownIds.EnvironmentFlags.WetlandAlive },
                    RewardActionDefinition.Score("ecology", 18),
                    new RewardActionDefinition { kind = RewardActionKind.UnlockNpc, targetId = "npc.mara_ecologist" },
                },
                titleKey = "project.ashfall.restore_wetland.title",
                descriptionKey = "project.ashfall.restore_wetland.desc",
            });

            _projects.Add(new RestorationProjectDefinition
            {
                projectId = "project.ashfall.revive_grove",
                regionId = RegionId,
                category = ProjectCategory.Ecosystem,
                vitalityCost = 240,
                prerequisiteProjectIds = { "project.ashfall.refill_river" },
                rewardActions =
                {
                    new RewardActionDefinition { kind = RewardActionKind.SetEnvironmentFlag, targetId = WellKnownIds.EnvironmentFlags.GroveRevived },
                    new RewardActionDefinition { kind = RewardActionKind.SetBuildingRestored, targetId = DeadGroveInstance },
                    RewardActionDefinition.Score("ecology", 15),
                },
                titleKey = "project.ashfall.revive_grove.title",
                descriptionKey = "project.ashfall.revive_grove.desc",
            });

            // --- Landmark: multi-stage transit gate (long-term motivation). ---
            _projects.Add(new RestorationProjectDefinition
            {
                projectId = "project.ashfall.transit_gate_awaken",
                regionId = RegionId,
                category = ProjectCategory.Landmark,
                vitalityCost = 500,
                prerequisiteProjectIds = { "project.ashfall.restore_research_hall", "project.ashfall.restore_house_east" },
                rewardActions =
                {
                    new RewardActionDefinition { kind = RewardActionKind.SetBuildingRestored, targetId = TransitGateInstance },
                    RewardActionDefinition.Score("knowledge", 10),
                    new RewardActionDefinition { kind = RewardActionKind.UnlockNpc, targetId = "npc.ivo_historian" },
                    new RewardActionDefinition { kind = RewardActionKind.DiscoverLore, targetId = "lore.ashfall.gate_inscription" },
                },
                titleKey = "project.ashfall.transit_gate_awaken.title",
                descriptionKey = "project.ashfall.transit_gate_awaken.desc",
            });

            _projects.Add(new RestorationProjectDefinition
            {
                projectId = "project.ashfall.transit_gate_align",
                regionId = RegionId,
                category = ProjectCategory.Landmark,
                vitalityCost = 800,
                prerequisiteProjectIds = { "project.ashfall.transit_gate_awaken" },
                requiredLifetimeSteps = 20000,
                rewardActions =
                {
                    RewardActionDefinition.Score("knowledge", 20),
                    new RewardActionDefinition { kind = RewardActionKind.UnlockNpc, targetId = "npc.bren_builder" },
                    // Region travel arrives with post-MVP content; the gate completion
                    // sequence is the vertical-slice finale (ROADMAP Phase 6).
                    new RewardActionDefinition { kind = RewardActionKind.GrantResource, secondaryId = WellKnownIds.Resources.Knowledge, amount = 50 },
                },
                titleKey = "project.ashfall.transit_gate_align.title",
                descriptionKey = "project.ashfall.transit_gate_align.desc",
            });
        }

        private void BuildProducers()
        {
            Register(Producer(WaterStationProducer, WellKnownIds.Resources.Water, 12, 600, (1, 1.0), (2, 1.5), (3, 2.25)));
            Register(Producer(GreenhouseProducer, WellKnownIds.Resources.Biomass, 8, 400, (1, 1.0), (2, 1.5), (3, 2.25)));
            Register(Producer(WorkshopProducer, WellKnownIds.Resources.Components, 5, 300, (1, 1.0), (2, 1.75)));
            Register(Producer(ResearchHallProducer, WellKnownIds.Resources.Knowledge, 3, 150, (1, 1.0), (2, 1.75)));
        }

        private void Register(ProducerDefinition producer)
        {
            _producers[producer.producerId] = producer;
            _producersById[producer.producerId] = producer;
            _producerList.Add(producer);
        }

        private void Index()
        {
            _buildingsById.Clear();
            foreach (var building in _buildings)
            {
                if (!string.IsNullOrEmpty(building?.definitionId))
                {
                    _buildingsById[building.definitionId] = building;
                }
            }
        }

        // IContentCatalog

        public RegionDefinition GetRegion(string regionId)
        {
            return regionId == RegionId ? Ashfall : null;
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
            return regionId == RegionId ? _projects : (IReadOnlyList<RestorationProjectDefinition>)Array.Empty<RestorationProjectDefinition>();
        }

        IReadOnlyList<MilestoneDefinition> IContentCatalog.GetMilestones()
        {
            return _milestones;
        }
    }
}
