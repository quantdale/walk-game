using System;
using System.Collections.Generic;
using WalkGame.Core;

namespace WalkGame.Persistence
{
    /// <summary>
    /// Reverts a canonical profile graph IN PLACE (ADR 0007). Application services,
    /// native providers, and scene actors hold references into the live profile's
    /// object tree; swapping the root reference would strand them on stale state.
    /// Copying durable values into the existing instances keeps every reference
    /// valid, which is what makes persistence-failure rollback safe at runtime.
    ///
    /// The copy is hand-written and explicit instead of reflection-based: this runs
    /// under IL2CPP managed stripping on device, where reflective construction of
    /// model members cannot be guaranteed. The compiler enforces completeness — a new
    /// field on a copied type fails nothing here, so the matching test asserts the
    /// copier against the serializer-visible graph and must be extended alongside any
    /// DATA_MODEL change (SaveIntegrityApplicationTests.CopyInto_MatchesSerializedGraph).
    /// </summary>
    public static class ProfileStateCopier
    {
        public static void CopyInto(PlayerProfile source, PlayerProfile target)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            target.schemaVersion = source.schemaVersion;
            target.profileId = source.profileId;
            target.createdAtUtc = source.createdAtUtc;
            target.lastSavedAtUtc = source.lastSavedAtUtc;
            target.lifetimeAcceptedSteps = source.lifetimeAcceptedSteps;
            target.lifetimeVerifiedDistanceMeters = source.lifetimeVerifiedDistanceMeters;
            target.vitalityBalance = source.vitalityBalance;

            CopyInto(source.resources, target.resources);
            CopyWorldState(source.worldState, target.worldState);
            CopyActivityState(source.activityState, target.activityState);
            CopyAchievementState(source.achievementState, target.achievementState);
            CopySettings(source.settings, target.settings);

            target.recentVitalityTransactions.Clear();
            foreach (var transaction in source.recentVitalityTransactions)
            {
                target.recentVitalityTransactions.Add(CopyTransaction(transaction));
            }
        }

        internal static void CopyWorldState(WorldState source, WorldState target)
        {
            if (source == null || target == null)
            {
                throw new ArgumentNullException(source == null ? nameof(source) : nameof(target));
            }

            target.currentEra = source.currentEra;
            target.currentRegionId = source.currentRegionId;

            target.unlockedRegionIds.Clear();
            foreach (var regionId in source.unlockedRegionIds)
            {
                target.unlockedRegionIds.Add(regionId);
            }

            // Replace map contents; never reassign the dictionary reference itself,
            // and REUSE existing value instances for surviving keys - services and
            // scene actors hold those objects directly.
            foreach (var pair in source.regionStates)
            {
                RegionState cloned;
                if (!target.regionStates.TryGetValue(pair.Key, out cloned))
                {
                    cloned = new RegionState();
                    target.regionStates.Add(pair.Key, cloned);
                }

                cloned.regionId = pair.Value.regionId;
                cloned.restorationStage = pair.Value.restorationStage;
                cloned.ecologyScore = pair.Value.ecologyScore;
                cloned.infrastructureScore = pair.Value.infrastructureScore;
                cloned.communityScore = pair.Value.communityScore;
                cloned.knowledgeScore = pair.Value.knowledgeScore;
                cloned.lastVisitedAtUtc = pair.Value.lastVisitedAtUtc;

                cloned.completedProjectIds.Clear();
                foreach (var id in pair.Value.completedProjectIds) { cloned.completedProjectIds.Add(id); }
                cloned.unlockedProjectIds.Clear();
                foreach (var id in pair.Value.unlockedProjectIds) { cloned.unlockedProjectIds.Add(id); }
                cloned.environmentFlags.Clear();
                foreach (var id in pair.Value.environmentFlags) { cloned.environmentFlags.Add(id); }
                cloned.discoveredLoreIds.Clear();
                foreach (var id in pair.Value.discoveredLoreIds) { cloned.discoveredLoreIds.Add(id); }
                cloned.arrivedNpcIds.Clear();
                foreach (var id in pair.Value.arrivedNpcIds) { cloned.arrivedNpcIds.Add(id); }

                foreach (var buildingPair in pair.Value.buildingStates)
                {
                    BuildingState buildingClone;
                    if (!cloned.buildingStates.TryGetValue(buildingPair.Key, out buildingClone))
                    {
                        buildingClone = new BuildingState();
                        cloned.buildingStates.Add(buildingPair.Key, buildingClone);
                    }

                    var sourceBuilding = buildingPair.Value;
                    buildingClone.instanceId = sourceBuilding.instanceId;
                    buildingClone.definitionId = sourceBuilding.definitionId;
                    buildingClone.lifecycleState = sourceBuilding.lifecycleState;
                    buildingClone.upgradeTier = sourceBuilding.upgradeTier;
                    buildingClone.restorationCompletedAtUtc = sourceBuilding.restorationCompletedAtUtc;
                    buildingClone.placement.gridX = sourceBuilding.placement.gridX;
                    buildingClone.placement.gridY = sourceBuilding.placement.gridY;
                    buildingClone.placement.rotationQuarterTurns = sourceBuilding.placement.rotationQuarterTurns;
                    buildingClone.placement.placementVersion = sourceBuilding.placement.placementVersion;
                }

                foreach (var producerPair in pair.Value.producerStates)
                {
                    ProducerState producerClone;
                    if (!cloned.producerStates.TryGetValue(producerPair.Key, out producerClone))
                    {
                        producerClone = new ProducerState();
                        cloned.producerStates.Add(producerPair.Key, producerClone);
                    }

                    producerClone.producerId = producerPair.Value.producerId;
                    producerClone.buildingInstanceId = producerPair.Value.buildingInstanceId;
                    producerClone.lastCheckpointUtc = producerPair.Value.lastCheckpointUtc;
                    producerClone.storedOutput = producerPair.Value.storedOutput;
                }
            }

            // Drop keys that vanished from the source AFTER reusing/cloning survivors.
            var staleRegionIds = new List<string>();
            foreach (var key in target.regionStates.Keys)
            {
                if (!source.regionStates.ContainsKey(key))
                {
                    staleRegionIds.Add(key);
                }
            }

            foreach (var stale in staleRegionIds)
            {
                target.regionStates.Remove(stale);
            }
        }

        internal static void CopyActivityState(ActivitySyncState source, ActivitySyncState target)
        {
            if (source == null || target == null)
            {
                throw new ArgumentNullException(source == null ? nameof(source) : nameof(target));
            }

            target.providerId = source.providerId;
            target.lastSuccessfulSyncUtc = source.lastSuccessfulSyncUtc;
            target.providerCursor = source.providerCursor;
            target.androidLastRawStepCounter = source.androidLastRawStepCounter;
            target.androidLastCounterObservedUtc = source.androidLastCounterObservedUtc;

            CopyKeys(source.creditedIntervals, target.creditedIntervals);
            CopyKeys(source.creditedSessionIds, target.creditedSessionIds);

            if (source.activeSession == null)
            {
                target.activeSession = null;
                return;
            }

            if (target.activeSession == null)
            {
                target.activeSession = new ActiveSessionState();
            }

            target.activeSession.sessionId = source.activeSession.sessionId;
            target.activeSession.sessionType = source.activeSession.sessionType;
            target.activeSession.startedAtUtc = source.activeSession.startedAtUtc;
            target.activeSession.initialStepBaseline = source.activeSession.initialStepBaseline;
            target.activeSession.accumulatedSteps = source.activeSession.accumulatedSteps;
            target.activeSession.accumulatedDistanceMeters = source.activeSession.accumulatedDistanceMeters;
            target.activeSession.movingSeconds = source.activeSession.movingSeconds;
        }

        private static void CopyKeys(CreditedActivityKeys source, CreditedActivityKeys target)
        {
            target.entries.Clear();
            target.entries.AddRange(source.entries);
            target.Rebuild();
        }

        private static void CopyAchievementState(AchievementState source, AchievementState target)
        {
            target.reachedMilestoneIds.Clear();
            foreach (var id in source.reachedMilestoneIds)
            {
                target.reachedMilestoneIds.Add(id);
            }

            target.lastGrowthBonusDayUtc = source.lastGrowthBonusDayUtc;
            target.tempoBonusesAwardedTodayUtc = source.tempoBonusesAwardedTodayUtc;
            target.tempoBonusCounterDayUtc = source.tempoBonusCounterDayUtc;
        }

        private static void CopySettings(PlayerSettings source, PlayerSettings target)
        {
            target.debugToolsEnabled = source.debugToolsEnabled;
            target.expeditionLocationOptIn = source.expeditionLocationOptIn;
            target.masterAudioVolume = source.masterAudioVolume;
            target.musicVolume = source.musicVolume;
            target.effectsVolume = source.effectsVolume;
            target.hapticsEnabled = source.hapticsEnabled;
            target.reducedMotion = source.reducedMotion;
            target.onboardingCompleted = source.onboardingCompleted;
            target.onboardingStep = source.onboardingStep;
        }

        private static VitalityTransaction CopyTransaction(VitalityTransaction source)
        {
            var clone = new VitalityTransaction();
            clone.transactionId = source.transactionId;
            clone.timestampUtc = source.timestampUtc;
            clone.type = source.type;
            clone.amount = source.amount;
            clone.reasonCode = source.reasonCode;
            clone.relatedEntityId = source.relatedEntityId;
            clone.resultingBalance = source.resultingBalance;
            return clone;
        }

        private static void CopyInto(Dictionary<string, long> source, Dictionary<string, long> target)
        {
            target.Clear();
            foreach (var pair in source)
            {
                target[pair.Key] = pair.Value;
            }
        }
    }
}
