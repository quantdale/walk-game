using System;
using System.Collections.Generic;

namespace WalkGame.Core
{
    /// <summary>
    /// Canonical player-owned save root. Mirrors DATA_MODEL.md section 2.
    /// Only derived gameplay values are persisted; raw sensor traces never are.
    /// </summary>
    public sealed class PlayerProfile
    {
        public int schemaVersion = SaveSchemaVersions.Current;
        public string profileId = Guid.NewGuid().ToString("D");
        // GameHost stamps this through its injected clock. A plain domain object must
        // not reach for the device wall clock during construction.
        public DateTime createdAtUtc = DateTime.MinValue;
        public DateTime lastSavedAtUtc = DateTime.MinValue;

        public long lifetimeAcceptedSteps;
        public double lifetimeVerifiedDistanceMeters;
        public long vitalityBalance;
        public Dictionary<string, long> resources = new Dictionary<string, long>();

        public WorldState worldState = new WorldState();
        public ActivitySyncState activityState = new ActivitySyncState();
        public AchievementState achievementState = new AchievementState();
        public PlayerSettings settings = new PlayerSettings();

        /// <summary>Bounded audit trail of recent balance mutations (DATA_MODEL.md 14).</summary>
        public List<VitalityTransaction> recentVitalityTransactions = new List<VitalityTransaction>();
    }

    public static class SaveSchemaVersions
    {
        /// <summary>Current save schema. Any breaking change must bump this and add a migration.</summary>
        public const int Current = 1;
    }

    public sealed class WorldState
    {
        public int currentEra;
        public string currentRegionId = WellKnownIds.StartingRegionId;
        public HashSet<string> unlockedRegionIds = new HashSet<string> { WellKnownIds.StartingRegionId };
        public Dictionary<string, RegionState> regionStates = new Dictionary<string, RegionState>();

        public bool TryGetRegionState(string regionId, out RegionState regionState)
        {
            return regionStates.TryGetValue(regionId ?? string.Empty, out regionState);
        }

        public RegionState GetOrCreateRegionState(string regionId)
        {
            if (!regionStates.TryGetValue(regionId, out var regionState))
            {
                regionState = new RegionState { regionId = regionId };
                regionStates[regionId] = regionState;
            }

            return regionState;
        }
    }

    public sealed class AchievementState
    {
        public HashSet<string> reachedMilestoneIds = new HashSet<string>();
        /// <summary>Last UTC day (yyyy-MM-dd) a growth bonus was awarded; empty when none.</summary>
        public string lastGrowthBonusDayUtc = string.Empty;
        public int tempoBonusesAwardedTodayUtc = 0;
        public string tempoBonusCounterDayUtc = string.Empty;
    }

    public sealed class PlayerSettings
    {
        public bool debugToolsEnabled;
        public bool expeditionLocationOptIn;
    }
}
