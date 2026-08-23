using System;
using WalkGame.Core;

namespace WalkGame.Persistence
{
    public sealed class SaveValidationReport
    {
        public int FutureRestorationTimestampCount { get; internal set; }

        public bool HasAnomalies => FutureRestorationTimestampCount > 0;
    }

    /// <summary>
    /// Load-time invariant repair/rejection (DATA_MODEL.md 20). Repairs are conservative:
    /// impossible values clamp to safe bounds, unresolvable references are pruned with a
    /// log entry, and nothing silently wipes player world state.
    /// </summary>
    public static class SaveValidator
    {
        public static SaveValidationReport RepairAndValidate(PlayerProfile profile, Log log)
        {
            return RepairAndValidate(profile, SystemClock.Instance, log);
        }

        public static SaveValidationReport RepairAndValidate(PlayerProfile profile, IClock clock, Log log)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            clock = clock ?? SystemClock.Instance;
            log = log ?? Log.Disabled;
            var report = new SaveValidationReport();
            var world = profile.worldState ?? new WorldState();
            profile.worldState = world;

            if (string.IsNullOrEmpty(world.currentRegionId))
            {
                world.currentRegionId = WellKnownIds.StartingRegionId;
            }

            if (!world.unlockedRegionIds.Contains(world.currentRegionId))
            {
                world.unlockedRegionIds.Add(world.currentRegionId);
            }

            if (profile.vitalityBalance < 0)
            {
                log.Warning($"Negative vitality balance {profile.vitalityBalance} clamped to zero.");
                profile.vitalityBalance = 0;
            }

            if (profile.lifetimeAcceptedSteps < 0)
            {
                profile.lifetimeAcceptedSteps = 0;
            }

            foreach (var pair in new System.Collections.Generic.Dictionary<string, long>(profile.resources))
            {
                if (pair.Value < 0)
                {
                    log.Warning($"Resource '{pair.Key}' negative; clamped.");
                    profile.resources[pair.Key] = 0;
                }
            }

            foreach (var regionPair in world.regionStates)
            {
                RepairRegion(regionPair.Value, clock, log, report);
            }

            // Dedup stores are additive schema fields (campaign S8): repair explicit
            // nulls from older/hand-edited saves and rebuild the membership indexes the
            // deserializer cannot populate (entries is a canonical serialized field).
            var activity = profile.activityState ?? new ActivitySyncState();
            profile.activityState = activity;
            (activity.creditedIntervals ??= new CreditedActivityKeys()).Rebuild();
            (activity.creditedSessionIds ??= new CreditedActivityKeys()).Rebuild();

            return report;
        }

        private static void RepairRegion(RegionState region, IClock clock, Log log, SaveValidationReport report)
        {
            if (region == null || string.IsNullOrEmpty(region.regionId))
            {
                return;
            }

            foreach (var buildingPair in new System.Collections.Generic.Dictionary<string, BuildingState>(region.buildingStates))
            {
                var building = buildingPair.Value;
                if (building == null || string.IsNullOrEmpty(building.definitionId) ||
                    building.instanceId != buildingPair.Key)
                {
                    log.Warning($"Pruning malformed building entry '{buildingPair.Key}'.");
                    region.buildingStates.Remove(buildingPair.Key);
                    continue;
                }

                var placement = building.placement ?? (building.placement = new BuildingPlacement());
                if (!IsFinite(placement.gridX) || !IsFinite(placement.gridY) ||
                    !IsFinite(placement.rotationQuarterTurns))
                {
                    log.Warning($"Non-finite placement on '{building.instanceId}' reset to origin.");
                    placement.gridX = 0;
                    placement.gridY = 0;
                    placement.rotationQuarterTurns = 0;
                }

                placement.rotationQuarterTurns = ((placement.rotationQuarterTurns % 4) + 4) % 4;

                // Timestamps far in the future are flagged for reconciliation.
                // Future timestamps are an anomaly for reconciliation. They remain in
                // the save so recovery never destroys state, but callers receive an
                // explicit report and the warning is observable in diagnostics.
                if (building.restorationCompletedAtUtc.HasValue &&
                    building.restorationCompletedAtUtc.Value > clock.UtcNow.AddDays(1))
                {
                    log.Warning($"Future restoration timestamp on '{building.instanceId}' flagged.");
                    report.FutureRestorationTimestampCount++;
                }
            }
        }

        private static bool IsFinite(int value)
        {
            // Grid coordinates are integers; guard against absurd values from corruption.
            return value >= -100000 && value <= 100000;
        }
    }
}
