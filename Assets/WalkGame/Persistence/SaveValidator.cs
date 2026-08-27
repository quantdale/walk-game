using System;
using WalkGame.Core;

namespace WalkGame.Persistence
{
    public sealed class SaveValidationReport
    {
        public int FutureRestorationTimestampCount { get; internal set; }

        /// <summary>M8.7 (H1): null RegionState map values rebuilt from the authoritative key.</summary>
        public int ReconstructedNullRegionStates { get; internal set; }

        /// <summary>M8.7 (H1): unreachable null RegionState map entries removed.</summary>
        public int PrunedUnreachableNullRegionStates { get; internal set; }

        /// <summary>M8.7 (H2): RegionState.regionId values normalized to their dictionary key.</summary>
        public int NormalizedRegionIdentityMismatches { get; internal set; }

        /// <summary>M8.7 (H3): null recentVitalityTransactions elements removed.</summary>
        public int PrunedNullTransactions { get; internal set; }

        public bool HasAnomalies =>
            FutureRestorationTimestampCount > 0 ||
            ReconstructedNullRegionStates > 0 ||
            PrunedUnreachableNullRegionStates > 0 ||
            NormalizedRegionIdentityMismatches > 0 ||
            PrunedNullTransactions > 0;
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
            profile.resources = profile.resources ?? new System.Collections.Generic.Dictionary<string, long>();
            profile.recentVitalityTransactions = profile.recentVitalityTransactions ?? new System.Collections.Generic.List<VitalityTransaction>();
            profile.achievementState = profile.achievementState ?? new AchievementState();
            // An explicit null list deserialized from JSON must be repaired here or the
            // next milestone award (and any failed-commit rollback copy) dereferences it.
            profile.achievementState.reachedMilestoneIds =
                profile.achievementState.reachedMilestoneIds ?? new System.Collections.Generic.HashSet<string>();
            profile.settings = profile.settings ?? new PlayerSettings();
            world.unlockedRegionIds = world.unlockedRegionIds ?? new System.Collections.Generic.HashSet<string>();
            world.regionStates = world.regionStates ?? new System.Collections.Generic.Dictionary<string, RegionState>();

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

            // Same corruption family as the step counter: a negative lifetime distance
            // would silently poison bounded run-bonus math downstream.
            if (profile.lifetimeVerifiedDistanceMeters < 0)
            {
                log.Warning("Negative lifetime distance clamped to zero.");
                profile.lifetimeVerifiedDistanceMeters = 0;
            }

            profile.settings.masterAudioVolume = Clamp01(profile.settings.masterAudioVolume, 1f);
            profile.settings.musicVolume = Clamp01(profile.settings.musicVolume, 0.8f);
            profile.settings.effectsVolume = Clamp01(profile.settings.effectsVolume, 1f);
            profile.settings.onboardingStep = Math.Max(0, profile.settings.onboardingStep);

            foreach (var pair in new System.Collections.Generic.Dictionary<string, long>(profile.resources))
            {
                if (pair.Value < 0)
                {
                    log.Warning($"Resource '{pair.Key}' negative; clamped.");
                    profile.resources[pair.Key] = 0;
                }
            }

            // M8.7 H1/H2: a parseable save can carry a null RegionState value or a
            // RegionState whose regionId disagrees with its dictionary key. Both
            // survive the old loop and later crash boot or create split canonical
            // identity. The dictionary key is the authoritative storage identity.
            foreach (var regionKey in new System.Collections.Generic.List<string>(world.regionStates.Keys))
            {
                if (!world.regionStates.TryGetValue(regionKey, out var region) || region == null)
                {
                    // A null/unreachable map entry is not recoverable state on its own.
                    // If the key is required (current or unlocked) reconstruct exactly
                    // that empty structural RegionState; otherwise prune it. No
                    // progression can be recovered from a null value.
                    if (regionKey == world.currentRegionId || world.unlockedRegionIds.Contains(regionKey))
                    {
                        log.Warning($"Reconstructed missing/null RegionState for required key '{regionKey}'.");
                        region = new RegionState { regionId = regionKey };
                        world.regionStates[regionKey] = region;
                        report.ReconstructedNullRegionStates++;
                    }
                    else
                    {
                        log.Warning($"Removing unreachable null RegionState entry '{regionKey}'.");
                        world.regionStates.Remove(regionKey);
                        report.PrunedUnreachableNullRegionStates++;
                        continue;
                    }
                }

                // H2: the key is authoritative storage identity. A conflicting regionId
                // is a corruption artifact; normalize it without inventing progression.
                if (region.regionId != regionKey)
                {
                    log.Warning($"Region identity normalized: value.regionId '{region.regionId}' -> key '{regionKey}'.");
                    region.regionId = regionKey;
                    report.NormalizedRegionIdentityMismatches++;
                }

                RepairRegion(region, clock, log, report);
            }

            // Dedup stores are additive schema fields (campaign S8): repair explicit
            // nulls from older/hand-edited saves and rebuild the membership indexes the
            // deserializer cannot populate (entries is a canonical serialized field).
            var activity = profile.activityState ?? new ActivitySyncState();
            profile.activityState = activity;
            (activity.creditedIntervals ??= new CreditedActivityKeys()).Rebuild();
            (activity.creditedSessionIds ??= new CreditedActivityKeys()).Rebuild();

            // M8.7 H3: the container-level repair above guarantees a non-null list,
            // but a parseable save can still contain explicit null elements. Those
            // survive into failed-commit rollback where ProfileStateCopier
            // dereferences every element. Remove nulls without synthesizing a
            // transaction or changing the canonical balance.
            int removedNullTransactions = 0;
            var compactedTransactions = new System.Collections.Generic.List<VitalityTransaction>();
            foreach (var transaction in profile.recentVitalityTransactions)
            {
                if (transaction == null)
                {
                    removedNullTransactions++;
                    continue;
                }

                compactedTransactions.Add(transaction);
            }

            if (removedNullTransactions > 0)
            {
                log.Warning($"Pruned {removedNullTransactions} null VitalityTransaction history element(s); no Vitality was minted.");
                profile.recentVitalityTransactions = compactedTransactions;
                report.PrunedNullTransactions += removedNullTransactions;
            }

            return report;
        }

        private static void RepairRegion(RegionState region, IClock clock, Log log, SaveValidationReport report)
        {
            if (region == null || string.IsNullOrEmpty(region.regionId))
            {
                return;
            }

            region.completedProjectIds = region.completedProjectIds ?? new System.Collections.Generic.HashSet<string>();
            region.unlockedProjectIds = region.unlockedProjectIds ?? new System.Collections.Generic.HashSet<string>();
            region.environmentFlags = region.environmentFlags ?? new System.Collections.Generic.HashSet<string>();
            region.buildingStates = region.buildingStates ?? new System.Collections.Generic.Dictionary<string, BuildingState>();
            region.discoveredLoreIds = region.discoveredLoreIds ?? new System.Collections.Generic.HashSet<string>();
            region.arrivedNpcIds = region.arrivedNpcIds ?? new System.Collections.Generic.HashSet<string>();
            region.producerStates = region.producerStates ?? new System.Collections.Generic.Dictionary<string, ProducerState>();

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

            // Producer entries mirror the building rules: prune malformed/null entries
            // and clamp impossible values so accrual math never sees poisoned inputs.
            foreach (var producerPair in new System.Collections.Generic.Dictionary<string, ProducerState>(region.producerStates))
            {
                var producer = producerPair.Value;
                if (producer == null || string.IsNullOrEmpty(producer.producerId) ||
                    producer.producerId != producerPair.Key)
                {
                    log.Warning($"Pruning malformed producer entry '{producerPair.Key}'.");
                    region.producerStates.Remove(producerPair.Key);
                    continue;
                }

                if (producer.storedOutput < 0)
                {
                    log.Warning($"Negative stored output on '{producer.producerId}' clamped to zero.");
                    producer.storedOutput = 0;
                }

                if (producer.lastCheckpointUtc > clock.UtcNow.AddHours(1))
                {
                    log.Warning($"Future production checkpoint on '{producer.producerId}' reset to now.");
                    producer.lastCheckpointUtc = clock.UtcNow;
                }
            }
        }

        private static bool IsFinite(int value)
        {
            // Grid coordinates are integers; guard against absurd values from corruption.
            return value >= -100000 && value <= 100000;
        }

        private static float Clamp01(float value, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return fallback;
            }

            return Math.Max(0f, Math.Min(1f, value));
        }
    }
}
