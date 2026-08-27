using System;
using System.Collections.Generic;
using NUnit.Framework;
using WalkGame.Core;
using WalkGame.Persistence;

namespace WalkGame.Tests.EditMode
{
    /// <summary>
    /// M8.7 canonical-state integrity-closure regressions (H1-H5 / S2-S9).
    /// These exercise the pure C# load/repair/rollback boundary headlessly so the
    /// campaign has current-run evidence even when no licensed Unity editor is
    /// present. Editor/device tiers remain a separate evidence tier.
    /// </summary>
    [TestFixture]
    public class M87SaveIntegrityClosureTests
    {
        private const string CurrentRegion = WellKnownIds.StartingRegionId;

        // ---- H1: null current-region RegionState must not crash boot ----------------

        [Test]
        public void H1_NullCurrentRegionState_ReconstructsAndResolvesAtBoot()
        {
            var profile = new PlayerProfile
            {
                worldState = new WorldState
                {
                    currentRegionId = CurrentRegion,
                    unlockedRegionIds = new HashSet<string> { CurrentRegion },
                    regionStates = new Dictionary<string, RegionState>
                    {
                        // Parseable save: key present, value null.
                        [CurrentRegion] = null,
                    },
                },
            };

            var report = SaveValidator.RepairAndValidate(profile, Log.Disabled);

            Assert.AreEqual(1, report.ReconstructedNullRegionStates, "required null region must be reconstructed");
            Assert.IsNotNull(profile.worldState.regionStates[CurrentRegion], "map value must no longer be null");
            Assert.AreEqual(CurrentRegion, profile.worldState.regionStates[CurrentRegion].regionId);

            // Boot-equivalent call chain (GameHost.EnsureRegionState -> GetOrCreateRegionState).
            var region = profile.worldState.GetOrCreateRegionState(profile.worldState.currentRegionId);
            Assert.DoesNotThrow(() =>
            {
                var _ = region.buildingStates;
                var __ = region.completedProjectIds;
            });
        }

        [Test]
        public void H1_NullUnlockedNonCurrentRegionState_Reconstructs()
        {
            const string other = "region.other";
            var profile = new PlayerProfile
            {
                worldState = new WorldState
                {
                    currentRegionId = CurrentRegion,
                    unlockedRegionIds = new HashSet<string> { CurrentRegion, other },
                    regionStates = new Dictionary<string, RegionState>
                    {
                        [CurrentRegion] = new RegionState { regionId = CurrentRegion },
                        [other] = null,
                    },
                },
            };

            var report = SaveValidator.RepairAndValidate(profile, Log.Disabled);

            Assert.AreEqual(1, report.ReconstructedNullRegionStates);
            Assert.IsNotNull(profile.worldState.regionStates[other]);
            Assert.AreEqual(other, profile.worldState.regionStates[other].regionId);
        }

        [Test]
        public void H1_NullUnreachableRegionState_Pruned()
        {
            const string ghost = "region.ghost";
            var profile = new PlayerProfile
            {
                worldState = new WorldState
                {
                    currentRegionId = CurrentRegion,
                    unlockedRegionIds = new HashSet<string> { CurrentRegion },
                    regionStates = new Dictionary<string, RegionState>
                    {
                        [CurrentRegion] = new RegionState { regionId = CurrentRegion },
                        [ghost] = null,
                    },
                },
            };

            var report = SaveValidator.RepairAndValidate(profile, Log.Disabled);

            Assert.AreEqual(1, report.PrunedUnreachableNullRegionStates);
            Assert.IsFalse(profile.worldState.regionStates.ContainsKey(ghost));
        }

        // ---- H2: region key / regionId identity coherence ---------------------------

        [Test]
        public void H2_RegionKeyMismatch_NormalizedToKey()
        {
            const string other = "region.other";
            var profile = new PlayerProfile
            {
                worldState = new WorldState
                {
                    currentRegionId = CurrentRegion,
                    unlockedRegionIds = new HashSet<string> { CurrentRegion, other },
                    regionStates = new Dictionary<string, RegionState>
                    {
                        [CurrentRegion] = new RegionState { regionId = CurrentRegion },
                        // Key says region.other but value claims region.somethingelse.
                        [other] = new RegionState { regionId = "region.somethingelse" },
                    },
                },
            };

            var report = SaveValidator.RepairAndValidate(profile, Log.Disabled);

            Assert.AreEqual(1, report.NormalizedRegionIdentityMismatches);
            Assert.AreEqual(other, profile.worldState.regionStates[other].regionId, "value identity must match storage key");
            Assert.IsTrue(profile.worldState.regionStates.ContainsKey(other));
        }

        [Test]
        public void H2_NoSplitIdentityDownstream()
        {
            const string other = "region.other";
            var profile = new PlayerProfile
            {
                worldState = new WorldState
                {
                    currentRegionId = CurrentRegion,
                    unlockedRegionIds = new HashSet<string> { CurrentRegion, other },
                    regionStates = new Dictionary<string, RegionState>
                    {
                        [CurrentRegion] = new RegionState { regionId = CurrentRegion },
                        [other] = new RegionState
                        {
                            regionId = "region.somethingelse",
                            completedProjectIds = new HashSet<string> { "project.x" },
                        },
                    },
                },
            };

            SaveValidator.RepairAndValidate(profile, Log.Disabled);

            var region = profile.worldState.regionStates[other];
            // Storage key and identity agree; progression under the key is preserved.
            Assert.AreEqual(other, region.regionId);
            Assert.IsTrue(region.completedProjectIds.Contains("project.x"));
        }

        // ---- H3: null transaction history must not crash rollback ------------------

        [Test]
        public void H3_NullTransactionElement_PrunedWithoutMinting()
        {
            var profile = new PlayerProfile
            {
                vitalityBalance = 42,
                recentVitalityTransactions = new List<VitalityTransaction>
                {
                    new VitalityTransaction { transactionId = "t1", amount = 10 },
                    null,
                    new VitalityTransaction { transactionId = "t2", amount = -3 },
                },
            };

            var report = SaveValidator.RepairAndValidate(profile, Log.Disabled);

            Assert.AreEqual(1, report.PrunedNullTransactions);
            Assert.AreEqual(2, profile.recentVitalityTransactions.Count, "null element removed");
            Assert.IsNotNull(profile.recentVitalityTransactions[0]);
            Assert.IsNotNull(profile.recentVitalityTransactions[1]);
            Assert.AreEqual(42, profile.vitalityBalance, "balance must not change during repair");
        }

        [Test]
        public void H3_CopyInto_ToleratesNullTransactionElement()
        {
            var source = new PlayerProfile
            {
                recentVitalityTransactions = new List<VitalityTransaction>
                {
                    new VitalityTransaction { transactionId = "a" },
                    null,
                },
            };
            var target = new PlayerProfile();

            Assert.DoesNotThrow(() => ProfileStateCopier.CopyInto(source, target));
            Assert.AreEqual(1, target.recentVitalityTransactions.Count);
            Assert.IsNotNull(target.recentVitalityTransactions[0]);
        }

        [Test]
        public void H3_FailedCommitWithNullTransactionDurable_RevertsWithoutException()
        {
            // Simulate a durable save that still carries a null transaction element
            // (the pre-fix danger: SaveValidator did not prune it at load, or a path
            // bypassed validation). The rollback copy must not throw.
            var durable = new PlayerProfile
            {
                vitalityBalance = 100,
                recentVitalityTransactions = new List<VitalityTransaction>
                {
                    new VitalityTransaction { transactionId = "kept" },
                    null,
                },
            };
            var repo = new NullTransactionDurableRepository(durable);
            var coordinator = new PersistenceCoordinator(repo, Log.Disabled, () => new PlayerProfile());

            var live = new PlayerProfile { vitalityBalance = 7 };
            PersistenceCommitOutcome outcome = PersistenceCommitOutcome.Committed;
            Assert.DoesNotThrow(() => outcome = coordinator.Commit(live));

            Assert.AreEqual(PersistenceCommitOutcome.RevertedToLastKnownGood, outcome);
            Assert.AreEqual(100, live.vitalityBalance, "last-known-good balance preserved");
            Assert.IsNotNull(live.recentVitalityTransactions);
            foreach (var tx in live.recentVitalityTransactions)
            {
                Assert.IsNotNull(tx, "rolled-back graph contains no null transactions");
            }
        }

        private sealed class NullTransactionDurableRepository : ISaveRepository
        {
            private readonly PlayerProfile _durable;
            public NullTransactionDurableRepository(PlayerProfile durable) => _durable = durable;
            public SaveLoadResult Save(PlayerProfile profile) => SaveLoadResult.Failed;
            public bool TryLoad(out PlayerProfile profile, out SaveLoadResult result)
            {
                profile = _durable;
                result = SaveLoadResult.Success;
                return true;
            }
            public bool MainSaveExists() => true;
            public bool BackupExists() => false;
            public void QuarantineAll() { }
        }

        // ---- S8: repaired graph round-trips and re-repair is idempotent -----------

        [Test]
        public void S8_RepairedProfile_RoundTripsAndReRepairIsIdempotent()
        {
            var profile = BuildMaximallyCorruptProfile();
            var first = SaveValidator.RepairAndValidate(profile, Log.Disabled);
            Assert.IsTrue(first.HasAnomalies, "precondition: at least one structural repair occurred");
            Assert.AreEqual(1, first.PrunedNullTransactions);

            var serializer = new JsonSaveSerializer();
            string json = null;
            Assert.DoesNotThrow(() => json = serializer.Serialize(profile));
            PlayerProfile reloaded = null;
            Assert.DoesNotThrow(() => reloaded = serializer.Deserialize(json));
            Assert.IsNotNull(reloaded);

            var second = SaveValidator.RepairAndValidate(reloaded, Log.Disabled);
            Assert.IsFalse(second.HasAnomalies, "re-repair of a canonical graph must be a no-op");
            Assert.AreEqual(0, second.ReconstructedNullRegionStates);
            Assert.AreEqual(0, second.PrunedUnreachableNullRegionStates);
            Assert.AreEqual(0, second.NormalizedRegionIdentityMismatches);
            Assert.AreEqual(0, second.PrunedNullTransactions);
        }

        // ---- H4 / S6: full serializer-visible structural invariant matrix ---------

        [Test]
        public void H4_StructuralInvariantMatrix_AllFamiliesClassified()
        {
            var profile = BuildMaximallyCorruptProfile();
            SaveValidator.RepairAndValidate(profile, Log.Disabled);

            // PlayerProfile root references
            Assert.IsNotNull(profile.resources);
            Assert.IsNotNull(profile.worldState);
            Assert.IsNotNull(profile.activityState);
            Assert.IsNotNull(profile.achievementState);
            Assert.IsNotNull(profile.settings);
            Assert.IsNotNull(profile.recentVitalityTransactions);

            // WorldState
            var world = profile.worldState;
            Assert.IsNotEmpty(world.currentRegionId);
            Assert.IsNotNull(world.unlockedRegionIds);
            Assert.IsTrue(world.unlockedRegionIds.Contains(world.currentRegionId));
            Assert.IsNotNull(world.regionStates);
            foreach (var pair in world.regionStates)
            {
                Assert.IsNotNull(pair.Value, "no surviving null RegionState");
                Assert.AreEqual(pair.Key, pair.Value.regionId, "key/identity coherent");
            }

            // RegionState sets/maps + building placement
            var region = world.regionStates[CurrentRegion];
            Assert.IsNotNull(region.completedProjectIds);
            Assert.IsNotNull(region.unlockedProjectIds);
            Assert.IsNotNull(region.environmentFlags);
            Assert.IsNotNull(region.buildingStates);
            Assert.IsNotNull(region.discoveredLoreIds);
            Assert.IsNotNull(region.arrivedNpcIds);
            Assert.IsNotNull(region.producerStates);
            var building = region.GetOrCreateBuildingState("building.probe", "def.probe");
            Assert.IsNotNull(building.placement);
            Assert.AreEqual(0, building.placement.gridX);
            Assert.AreEqual(0, building.placement.gridY);

            // ActivitySyncState dedup containers rebuilt; activeSession legitimate null
            Assert.IsNotNull(profile.activityState.creditedIntervals);
            Assert.IsNotNull(profile.activityState.creditedSessionIds);
            Assert.AreEqual(0, profile.activityState.creditedIntervals.entries.Count);
            profile.activityState.activeSession = null; // explicitly legitimate
            SaveValidator.RepairAndValidate(profile, Log.Disabled);
            Assert.IsNull(profile.activityState.activeSession, "legitimate null activeSession preserved");

            // AchievementState / Settings
            Assert.IsNotNull(profile.achievementState.reachedMilestoneIds);
            Assert.IsNotNull(profile.settings);

            // VitalityTransaction elements non-null
            foreach (var tx in profile.recentVitalityTransactions)
            {
                Assert.IsNotNull(tx);
            }
        }

        [Test]
        public void S7_RepairDoesNotMintProgression()
        {
            var profile = BuildMaximallyCorruptProfile();
            long beforeBalance = profile.vitalityBalance;
            int beforeCompleted = profile.worldState.regionStates[CurrentRegion].completedProjectIds.Count;
            int beforeResources = profile.resources.Count;

            SaveValidator.RepairAndValidate(profile, Log.Disabled);

            Assert.AreEqual(beforeBalance, profile.vitalityBalance, "no Vitality minted");
            Assert.AreEqual(beforeCompleted, profile.worldState.regionStates[CurrentRegion].completedProjectIds.Count, "no project completed");
            Assert.AreEqual(beforeResources, profile.resources.Count, "no resources granted");
            Assert.IsFalse(profile.achievementState.reachedMilestoneIds.Count > 0 && profile.achievementState.reachedMilestoneIds.Contains("milestone.injected"), "no achievement awarded");
        }

        private static PlayerProfile BuildMaximallyCorruptProfile()
        {
            var profile = new PlayerProfile
            {
                vitalityBalance = 13,
                resources = new Dictionary<string, long> { ["wood"] = 5 },
                worldState = new WorldState
                {
                    currentRegionId = CurrentRegion,
                    unlockedRegionIds = new HashSet<string> { CurrentRegion },
                    regionStates = new Dictionary<string, RegionState>
                    {
                        [CurrentRegion] = new RegionState
                        {
                            regionId = CurrentRegion,
                            completedProjectIds = new HashSet<string> { "project.existing" },
                            buildingStates = new Dictionary<string, BuildingState>
                            {
                                ["building.probe"] = new BuildingState
                                {
                                    instanceId = "building.probe",
                                    definitionId = "def.probe",
                                    placement = null,
                                },
                            },
                            producerStates = new Dictionary<string, ProducerState>
                            {
                                ["producer.probe"] = new ProducerState
                                {
                                    producerId = "producer.probe",
                                    buildingInstanceId = "building.probe",
                                },
                            },
                        },
                    },
                },
                activityState = new ActivitySyncState(),
                achievementState = new AchievementState(),
                settings = new PlayerSettings(),
                recentVitalityTransactions = new List<VitalityTransaction>
                {
                    new VitalityTransaction { transactionId = "t1", amount = 1 },
                    null,
                },
            };
            return profile;
        }
    }
}
