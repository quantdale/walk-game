using System;
using System.Collections.Generic;

namespace WalkGame.Core
{
    /// <summary>
    /// Immutable content definitions. ScriptableObjects in the Content assembly surface
    /// these plain records; services only ever see these Unity-free types.
    /// </summary>
    public sealed class FootprintDefinition
    {
        public int widthCells = 1;
        public int depthCells = 1;

        public int CellCount => widthCells * depthCells;
    }

    public sealed class UpgradeTierDefinition
    {
        public int tier = 1;
        public long vitalityCost;
        public Dictionary<string, long> resourceCosts = new Dictionary<string, long>();
        public double productionMultiplier = 1.0;
        public string titleKey = string.Empty;
    }

    public sealed class BuildingDefinition
    {
        public string definitionId = string.Empty;
        public string displayNameKey = string.Empty;
        public FootprintDefinition footprint = new FootprintDefinition();
        public bool movableAfterRestore = true;
        public int maxUpgradeTier = 1;
        public string producerDefinitionId;
        public List<UpgradeTierDefinition> upgradeDefinitions = new List<UpgradeTierDefinition>();

        public bool TryGetUpgrade(int tier, out UpgradeTierDefinition upgrade)
        {
            upgrade = null;
            foreach (var candidate in upgradeDefinitions)
            {
                if (candidate != null && candidate.tier == tier)
                {
                    upgrade = candidate;
                    return true;
                }
            }

            return false;
        }
    }

    public sealed class ProducerDefinition
    {
        public string producerId = string.Empty;
        public string resourceId = WellKnownIds.Resources.Biomass;
        public double baseRatePerHour;
        /// <summary>Maximum units the producer can hold before collection.</summary>
        public long storageCap = 500;
        /// <summary>Offline production window cap in hours (MASTER_PLAN section 7: 8-12h).</summary>
        public double offlineCapHours = 8.0;
        public Dictionary<int, double> tierMultipliers = new Dictionary<int, double>();

        public double MultiplierForTier(int tier)
        {
            return tierMultipliers.TryGetValue(tier, out var multiplier) ? multiplier : 1.0;
        }
    }

    public sealed class DefaultBuildingInstanceDefinition
    {
        public string instanceId = string.Empty;
        public string buildingDefinitionId = string.Empty;
        public BuildingPlacement initialPlacement = new BuildingPlacement();
        public bool startsRestored;
        /// <summary>Fixed structures (dams, bridges, transit gates) can never be moved.</summary>
        public bool fixedPlacement;
        public string producerId;
    }

    public enum ProjectCategory
    {
        Micro = 0,
        Building = 1,
        Ecosystem = 2,
        Landmark = 3,
        Era = 4
    }

    public enum RewardActionKind
    {
        UnlockBuilding = 0,
        SetBuildingRestored = 1,
        UnlockProject = 2,
        AddRegionScore = 3,
        UnlockNpc = 4,
        SetEnvironmentFlag = 5,
        UnlockRegion = 6,
        GrantResource = 7,
        DiscoverLore = 8
    }

    /// <summary>Data-driven reward action; avoids giant switch statements growing per feature.</summary>
    public sealed class RewardActionDefinition
    {
        public RewardActionKind kind;
        /// <summary>Primary target: building instance, project, NPC id, region id, score type or flag id.</summary>
        public string targetId = string.Empty;
        /// <summary>Secondary target for AddRegionScore (score name) or amount carrier for grants.</summary>
        public string secondaryId = string.Empty;
        public long amount;

        public static RewardActionDefinition Score(string scoreType, int delta)
        {
            return new RewardActionDefinition { kind = RewardActionKind.AddRegionScore, targetId = scoreType, amount = delta };
        }
    }

    public sealed class RestorationProjectDefinition
    {
        public string projectId = string.Empty;
        public string regionId = string.Empty;
        public ProjectCategory category = ProjectCategory.Micro;
        public long vitalityCost;
        public Dictionary<string, long> resourceCosts = new Dictionary<string, long>();
        public List<string> prerequisiteProjectIds = new List<string>();
        public long? requiredLifetimeSteps;
        public int? requiredRegionStage;
        public List<RewardActionDefinition> rewardActions = new List<RewardActionDefinition>();
        public string visualStageId;
        public string titleKey = string.Empty;
        public string descriptionKey = string.Empty;

        public bool IsLandmark => category == ProjectCategory.Landmark;
    }

    public sealed class StageThresholdDefinition
    {
        /// <summary>Restoration stage this threshold unlocks (0-based).</summary>
        public int stage = 1;
        /// <summary>Sum of region scores required to advance.</summary>
        public int totalScoreRequired;
        /// <summary>Projects that must all be complete before the stage advances.</summary>
        public List<string> requiredProjectIds = new List<string>();
        public string visualProfileId = string.Empty;
    }

    public sealed class NpcDefinition
    {
        public string npcId = string.Empty;
        public string displayNameKey = string.Empty;
        public string roleKey = string.Empty;
        /// <summary>Project that must complete before this NPC arrives.</summary>
        public string arrivalPrerequisiteProjectId = string.Empty;
        public string spawnAnchorId = string.Empty;
    }

    public sealed class LoreDefinition
    {
        public string loreId = string.Empty;
        public string titleKey = string.Empty;
        public string bodyKey = string.Empty;
        /// <summary>Optional project that must be complete before the lore object is discoverable.</summary>
        public string prerequisiteProjectId = string.Empty;
    }

    public sealed class MilestoneDefinition
    {
        public string milestoneId = string.Empty;
        public long lifetimeStepsRequired;
        public long vitalityReward;
        public string titleKey = string.Empty;
    }

    /// <summary>
    /// Read-only view over authored content consumed by domain services.
    /// The Content assembly implements this from ScriptableObjects; tests build it directly.
    /// </summary>
    public interface IContentCatalog
    {
        RegionDefinition GetRegion(string regionId);
        BuildingDefinition GetBuilding(string definitionId);
        ProducerDefinition GetProducer(string producerId);
        IReadOnlyList<RestorationProjectDefinition> GetProjectsForRegion(string regionId);
        IReadOnlyList<MilestoneDefinition> GetMilestones();
    }
}
