using System;
using System.Collections.Generic;

namespace WalkGame.Core
{
    /// <summary>
    /// Mutable player-owned state of one region. Mirrors DATA_MODEL.md section 4.
    /// Invariant: regionId must correspond to one immutable RegionDefinition.
    /// Both Builder and Explore views project this state; neither owns it.
    /// </summary>
    public sealed class RegionState
    {
        public string regionId = string.Empty;
        public int restorationStage;
        public int ecologyScore;
        public int infrastructureScore;
        public int communityScore;
        public int knowledgeScore;
        public HashSet<string> completedProjectIds = new HashSet<string>();
        /// <summary>Projects explicitly unlocked by reward actions (available to start).</summary>
        public HashSet<string> unlockedProjectIds = new HashSet<string>();
        public Dictionary<string, BuildingState> buildingStates = new Dictionary<string, BuildingState>();
        public HashSet<string> discoveredLoreIds = new HashSet<string>();
        public HashSet<string> arrivedNpcIds = new HashSet<string>();
        public Dictionary<string, ProducerState> producerStates = new Dictionary<string, ProducerState>();
        public DateTime lastVisitedAtUtc = DateTime.MinValue;

        public BuildingState GetOrCreateBuildingState(string instanceId, string definitionId)
        {
            if (!buildingStates.TryGetValue(instanceId, out var building))
            {
                building = new BuildingState
                {
                    instanceId = instanceId,
                    definitionId = definitionId,
                };
                buildingStates[instanceId] = building;
            }

            return building;
        }
    }

    public enum BuildingLifecycleState
    {
        Ruin = 0,
        Restoring = 1,
        Restored = 2
    }

    public sealed class BuildingState
    {
        public string instanceId = string.Empty;
        public string definitionId = string.Empty;
        public BuildingLifecycleState lifecycleState = BuildingLifecycleState.Ruin;
        public int upgradeTier;
        public BuildingPlacement placement = new BuildingPlacement();
        public DateTime? restorationCompletedAtUtc;

        public bool IsRestored => lifecycleState == BuildingLifecycleState.Restored;
    }

    /// <summary>
    /// Grid placement is canonical (deterministic and migration-friendly per DATA_MODEL.md 6).
    /// Local world position derives from grid coordinates at presentation time.
    /// </summary>
    public sealed class BuildingPlacement
    {
        public int gridX;
        public int gridY;
        public int rotationQuarterTurns;
        public int placementVersion;
    }

    public sealed class ProducerState
    {
        public string producerId = string.Empty;
        public string buildingInstanceId = string.Empty;
        public DateTime lastCheckpointUtc = DateTime.MinValue;
        public long storedOutput;
    }
}
