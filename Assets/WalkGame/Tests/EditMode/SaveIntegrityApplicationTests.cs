using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using WalkGame.Core;
using WalkGame.Gameplay;
using WalkGame.Persistence;

namespace WalkGame.Tests
{
    /// <summary>
    /// M8.1 save-integrity campaign: proves the APPLICATION-level persistence-health
    /// contract without Unity (ADR 0007) - boot policy mapping, transactional commit
    /// containment, in-place rollback semantics, and the rollback copier's fidelity to
    /// the serialized save graph.
    /// </summary>
    public sealed class SaveIntegrityApplicationTests
    {
        private string _directory;
        private MutableClock _clock;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "walkgame-tests", Guid.NewGuid().ToString("N"));
            _clock = new MutableClock(new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        // ------------------------------------------------------------------ policy

        [Test]
        public void BootPolicy_MapsEveryLoadResultToItsHealthState()
        {
            Assert.AreEqual(PersistenceHealth.Healthy,
                PersistencePolicy.HealthForBoot(SaveLoadResult.Success));
            Assert.AreEqual(PersistenceHealth.Fresh,
                PersistencePolicy.HealthForBoot(SaveLoadResult.Empty));
            Assert.AreEqual(PersistenceHealth.Recovered,
                PersistencePolicy.HealthForBoot(SaveLoadResult.RecoveredFromBackup));

            // Every fatal state blocks durable mutation - including a recovery whose
            // main slot holds newer-schema evidence.
            foreach (var result in new[]
                     {
                         SaveLoadResult.Failed,
                         SaveLoadResult.IncompatibleSchema,
                         SaveLoadResult.RecoveredFromBackupForwardSchema,
                     })
            {
                Assert.AreEqual(PersistenceHealth.Blocked, PersistencePolicy.HealthForBoot(result),
                    $"{result} must boot fail-closed");
                Assert.IsFalse(PersistencePolicy.AllowsDurableMutation(PersistenceHealth.Blocked));
            }

            Assert.IsTrue(PersistencePolicy.AllowsDurableMutation(PersistenceHealth.Fresh));
            Assert.IsTrue(PersistencePolicy.AllowsDurableMutation(PersistenceHealth.Healthy));
            Assert.IsTrue(PersistencePolicy.AllowsDurableMutation(PersistenceHealth.Recovered));
        }

        // -------------------------------------------------------------- coordinator

        [Test]
        public void Commit_WriteFailure_RevertsLiveGraphToExactDurableState_KeepsExactlyOnceConsistent()
        {
            var repository = CreateCleanRepository();
            var durable = BuildDurableProfile(out _, out var intervalKey);
            Assert.AreEqual(SaveLoadResult.Success, repository.Save(durable));
            long durableVitality = durable.vitalityBalance;

            Assert.IsTrue(repository.TryLoad(out var live, out _));

            // Simulate a gameplay window: reward + project completion + dedup advance.
            live.vitalityBalance += 250;
            live.worldState.GetOrCreateRegionState("region.ashfall").completedProjectIds.Add("project.x");
            Assert.IsTrue(live.activityState.creditedIntervals.TryMarkCredited(intervalKey + ":next"));

            var coordinator = new PersistenceCoordinator(
                CreateFailingRepository("move"), Log.Disabled, () => throw new InvalidOperationException("not used"));
            Assert.AreEqual(PersistenceCommitOutcome.RevertedToLastKnownGood, coordinator.Commit(live));

            Assert.AreEqual(durableVitality, live.vitalityBalance, "balance must equal disk truth");
            CollectionAssert.DoesNotContain(
                live.worldState.GetOrCreateRegionState("region.ashfall").completedProjectIds, "project.x");
            Assert.IsFalse(live.activityState.creditedIntervals.Contains(intervalKey + ":next"),
                "the uncredited window key must be freed by the rollback");

            // Exactly-once corollary: replaying the same snapshot after rollback credits
            // exactly once onto the reverted base instead of double-paying or losing it.
            Assert.IsTrue(live.activityState.creditedIntervals.TryMarkCredited(intervalKey + ":next"));
            Assert.IsTrue(live.activityState.creditedIntervals.Contains(intervalKey),
                "the original durable key stays consumed");
        }

        [Test]
        public void Commit_FailingFreshSession_RevertsToPristineFactoryProfile()
        {
            var pristineCreatedAt = _clock.UtcNow.AddMinutes(-5);
            var live = BuildDurableProfile(out _, out _);
            live.vitalityBalance += 77;
            live.lifetimeAcceptedSteps += 5000;

            var coordinator = new PersistenceCoordinator(
                CreateFailingRepository("write"),
                Log.Disabled,
                () => new PlayerProfile { schemaVersion = SaveSchemaVersions.Current, createdAtUtc = pristineCreatedAt });

            Assert.AreEqual(PersistenceCommitOutcome.RevertedToLastKnownGood, coordinator.Commit(live));
            Assert.AreEqual(0, live.vitalityBalance);
            Assert.AreEqual(0, live.lifetimeAcceptedSteps);
            Assert.AreEqual(pristineCreatedAt, live.createdAtUtc);
            Assert.IsNotNull(live.worldState);
            Assert.IsNotNull(live.activityState.creditedIntervals);
        }

        [Test]
        public void Commit_UnsaveableAndUnreadableDisk_ReportsFatalLoss_SoHostCanFailClosed()
        {
            var repository = CreateCleanRepository();
            Assert.AreEqual(SaveLoadResult.Success, repository.Save(BuildDurableProfile(out _, out _)));

            File.WriteAllText(Path.Combine(_directory, "profile.json"), "{ broken");
            File.WriteAllText(Path.Combine(_directory, "profile.json.bak"), "{ broken too");

            Assert.IsFalse(repository.TryLoad(out _, out var loadResult),
                "both slots unreadable must report failure");
            Assert.AreEqual(SaveLoadResult.Failed, loadResult);

            // A live in-memory session still exists when the commit runs; with storage
            // also refusing writes there is nothing left to revert or recover to.
            var live = BuildDurableProfile(out _, out _);
            var coordinator = new PersistenceCoordinator(
                CreateFailingRepository("write"), Log.Disabled, null);
            Assert.AreEqual(PersistenceCommitOutcome.FatalPersistenceLoss, coordinator.Commit(live));
            Assert.AreEqual(loadResult, coordinator.LastFailure);
        }

        [Test]
        public void Commit_OverAllCorruptSlots_SelfHealsByQuarantiningAndReestablishingTrust()
        {
            var durable = BuildDurableProfile(out _, out _);
            var repository = CreateCleanRepository();
            Assert.AreEqual(SaveLoadResult.Success, repository.Save(durable));
            long durableVitality = durable.vitalityBalance;

            // Disk rots mid-session AFTER a good load; the live profile remains the
            // newest known-good state, so committing over the rotten slots must
            // succeed by quarantining the garbage - not report a fatal loss.
            File.WriteAllText(Path.Combine(_directory, "profile.json"), "{ rot");
            File.WriteAllText(Path.Combine(_directory, "profile.json.bak"), "{ rot too");

            var live = BuildDurableProfile(out _, out _);
            live.vitalityBalance += 10;
            var coordinator = new PersistenceCoordinator(repository, Log.Disabled, null);
            Assert.AreEqual(PersistenceCommitOutcome.Committed, coordinator.Commit(live));

            Assert.IsTrue(CreateCleanRepository().TryLoad(out var reloaded, out var loadResult));
            Assert.AreEqual(SaveLoadResult.Success, loadResult);
            Assert.AreEqual(durableVitality + 10, reloaded.vitalityBalance);
            Assert.IsTrue(File.Exists(Path.Combine(_directory, "profile.json.quarantined")),
                "rotten evidence is preserved, never deleted");
        }

        // ------------------------------------------------------------- copier core

        [Test]
        public void CopyInto_PreservesInstanceIdentity_OfSharedSubObjects()
        {
            var target = BuildDurableProfile(out var region, out _);
            var worldBefore = target.worldState;
            var activityBefore = target.activityState;
            var regionBefore = target.worldState.regionStates["region.ashfall"];

            var source = BuildDurableProfile(out _, out _);
            source.vitalityBalance = 9999;

            ProfileStateCopier.CopyInto(source, target);

            Assert.AreSame(worldBefore, target.worldState, "services hold the world reference");
            Assert.AreSame(activityBefore, target.activityState, "native providers hold the activity reference");
            Assert.AreSame(regionBefore, region);
            Assert.AreSame(regionBefore, target.worldState.regionStates["region.ashfall"]);
            Assert.AreEqual(9999, target.vitalityBalance, "values are replaced in place");
        }

        [Test]
        public void CopyInto_ReplacesCollectionContents_AndRebuildsDedupMembership()
        {
            var target = BuildDurableProfile(out _, out var staleKey);
            var source = BuildDurableProfile(out _, out _);
            source.activityState.creditedIntervals.entries.Clear();
            source.activityState.creditedIntervals.entries.Add("fresh:key");

            ProfileStateCopier.CopyInto(source, target);

            CollectionAssert.AreEqual(new[] { "fresh:key" }, target.activityState.creditedIntervals.entries);
            Assert.IsFalse(target.activityState.creditedIntervals.Contains(staleKey),
                "membership index must match the copied entries after repair");
            Assert.IsTrue(target.activityState.creditedIntervals.Contains("fresh:key"));
            Assert.AreEqual(1, target.activityState.creditedIntervals.Count);

            // Nested dictionaries and per-element clones follow the source shape.
            var region = target.worldState.regionStates["region.ashfall"];
            var sourceRegion = source.worldState.regionStates["region.ashfall"];
            Assert.AreEqual(sourceRegion.completedProjectIds.Count, region.completedProjectIds.Count);
            Assert.AreEqual(sourceRegion.buildingStates["building.pump"].placement.gridX,
                region.buildingStates["building.pump"].placement.gridX);
            Assert.AreEqual(sourceRegion.producerStates["producer.pump"].storedOutput,
                region.producerStates["producer.pump"].storedOutput);
        }

        [Test]
        public void CopyInto_MatchesSerializedGraph_Exactly()
        {
            // Graph-fidelity gate: every serializer-visible field must be copied, so
            // serializing the rollback target reproduces the parsed source payload
            // byte-for-byte. Extend BuildFullyPopulatedProfile AND this test together
            // with any DATA_MODEL change.
            var serializer = new JsonSaveSerializer();
            var source = BuildFullyPopulatedProfile();
            string sourcePayload = serializer.Serialize(source);

            var parsedSource = serializer.Deserialize(sourcePayload);
            // The production load path repairs derived indexes via SaveValidator;
            // mirror that here so both sides describe repaired canonical state.
            parsedSource.activityState.creditedIntervals.Rebuild();
            parsedSource.activityState.creditedSessionIds.Rebuild();
            string reparsedPayload = serializer.Serialize(parsedSource);
            var rollbackTarget = new PlayerProfile();

            ProfileStateCopier.CopyInto(parsedSource, rollbackTarget);

            Assert.AreEqual(reparsedPayload, serializer.Serialize(rollbackTarget),
                "rollback target diverged from the serialized source graph");
            Assert.AreEqual(source.schemaVersion, rollbackTarget.schemaVersion);
        }

        [Test]
        public void CopyInto_NullableFields_CopyExactly_IncludingNulls()
        {
            var target = BuildDurableProfile(out _, out _);
            var source = BuildDurableProfile(out _, out _);
            source.activityState.activeSession = new ActiveSessionState { initialStepBaseline = 55 };

            source.activityState.androidLastRawStepCounter = null;
            source.activityState.lastSuccessfulSyncUtc = null;
            source.activityState.activeSession.initialStepBaseline = null;

            ProfileStateCopier.CopyInto(source, target);

            Assert.IsNull(target.activityState.androidLastRawStepCounter);
            Assert.IsNull(target.activityState.lastSuccessfulSyncUtc);
            Assert.IsNull(target.activityState.activeSession.initialStepBaseline);

            source.activityState.androidLastRawStepCounter = 4321.5;
            source.activityState.activeSession.initialStepBaseline = 17;
            ProfileStateCopier.CopyInto(source, target);
            Assert.AreEqual(4321.5, target.activityState.androidLastRawStepCounter.GetValueOrDefault());
            Assert.AreEqual(17, target.activityState.activeSession.initialStepBaseline.GetValueOrDefault());
        }

        // ---------------------------------------------------------------- helpers

        private FileSaveRepository CreateCleanRepository()
        {
            return new FileSaveRepository(
                _directory, "profile.json", new JsonSaveSerializer(), new SaveMigrator(), Log.Disabled, _clock);
        }

        private FileSaveRepository CreateFailingRepository(string failOperation)
        {
            return new FileSaveRepository(
                _directory, "profile.json", new JsonSaveSerializer(), new SaveMigrator(),
                Log.Disabled, _clock, new SingleOperationFaultFileSystem(failOperation));
        }

        /// <summary>A durable-looking baseline: credited steps, restored building, producer, dedup keys.</summary>
        private PlayerProfile BuildDurableProfile(out RegionState region, out string intervalKey)
        {
            var profile = new PlayerProfile { schemaVersion = SaveSchemaVersions.Current, createdAtUtc = _clock.UtcNow };
            var ledger = new VitalityLedger(profile, _clock, new DomainEvents(), Log.Disabled);
            ledger.Credit(VitalityCredit.Steps(3000));

            intervalKey = "activity.debug:2026-08-01T07:00:00.0000000Z:2026-08-01T08:00:00.0000000Z";
            Assert.IsTrue(profile.activityState.creditedIntervals.TryMarkCredited(intervalKey));
            profile.activityState.androidLastRawStepCounter = 12345.5;

            region = profile.worldState.GetOrCreateRegionState("region.ashfall");
            var pump = region.GetOrCreateBuildingState("building.pump", "building.pump.def");
            pump.lifecycleState = BuildingLifecycleState.Restored;
            pump.placement.gridX = 4;
            region.producerStates["producer.pump"] = new ProducerState
            {
                producerId = "producer.pump",
                buildingInstanceId = "building.pump",
                lastCheckpointUtc = _clock.UtcNow,
                storedOutput = 12,
            };
            region.completedProjectIds.Add("project.first");
            return profile;
        }

        /// <summary>Touches EVERY persisted model area so the graph-fidelity gate is meaningful.</summary>
        private static PlayerProfile BuildFullyPopulatedProfile()
        {
            var profile = BuildDurableBaseline();
            profile.profileId = "11111111-2222-3333-4444-555555555555";
            profile.lastSavedAtUtc = new DateTime(2026, 8, 1, 9, 30, 0, DateTimeKind.Utc);
            profile.resources[WellKnownIds.Resources.Biomass] = 42;
            profile.resources[WellKnownIds.Resources.Water] = 7;

            var region = profile.worldState.regionStates["region.ashfall"];
            region.restorationStage = 2;
            region.ecologyScore = 10;
            region.infrastructureScore = 20;
            region.communityScore = 30;
            region.knowledgeScore = 40;
            region.lastVisitedAtUtc = new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc);
            region.unlockedProjectIds.Add("project.second");
            region.environmentFlags.Add("flag.grove_lit");
            region.discoveredLoreIds.Add("lore.first");
            region.arrivedNpcIds.Add("npc.keeper");
            region.buildingStates["building.pump"].restorationCompletedAtUtc =
                new DateTime(2026, 7, 31, 18, 0, 0, DateTimeKind.Utc);
            region.buildingStates["building.pump"].upgradeTier = 1;

            profile.worldState.currentEra = 1;
            profile.worldState.unlockedRegionIds.Add("region.coast");

            profile.activityState.providerId = "debug";
            profile.activityState.lastSuccessfulSyncUtc = new DateTime(2026, 8, 1, 8, 30, 0, DateTimeKind.Utc);
            profile.activityState.providerCursor = "cursor-token";
            profile.activityState.androidLastCounterObservedUtc = new DateTime(2026, 8, 1, 8, 45, 0, DateTimeKind.Utc);
            profile.activityState.creditedSessionIds.entries.Add("session:abc");
            profile.activityState.activeSession = new ActiveSessionState();
            profile.activityState.activeSession.sessionType = SessionType.Run;
            profile.activityState.activeSession.startedAtUtc = new DateTime(2026, 8, 1, 8, 50, 0, DateTimeKind.Utc);
            profile.activityState.activeSession.initialStepBaseline = 100;
            profile.activityState.activeSession.accumulatedSteps = 2500;
            profile.activityState.activeSession.accumulatedDistanceMeters = 1800.5;
            profile.activityState.activeSession.movingSeconds = 1500.25;

            profile.achievementState.reachedMilestoneIds.Add("milestone.first_steps");
            profile.achievementState.lastGrowthBonusDayUtc = "2026-08-01";
            profile.achievementState.tempoBonusesAwardedTodayUtc = 2;
            profile.achievementState.tempoBonusCounterDayUtc = "2026-08-01";

            profile.settings.debugToolsEnabled = true;
            profile.settings.expeditionLocationOptIn = true;
            profile.settings.masterAudioVolume = 0.5f;
            profile.settings.musicVolume = 0.25f;
            profile.settings.effectsVolume = 0.75f;
            profile.settings.hapticsEnabled = false;
            profile.settings.reducedMotion = true;
            profile.settings.onboardingCompleted = true;
            profile.settings.onboardingStep = 6;

            profile.recentVitalityTransactions.Add(new VitalityTransaction
            {
                transactionId = "tx-1",
                timestampUtc = new DateTime(2026, 8, 1, 7, 59, 0, DateTimeKind.Utc),
                type = LedgerTransactionType.Credit,
                amount = 100,
                reasonCode = WellKnownIds.ReasonCodes.Steps,
                relatedEntityId = null,
                resultingBalance = 100,
            });
            profile.recentVitalityTransactions.Add(new VitalityTransaction
            {
                transactionId = "tx-2",
                timestampUtc = new DateTime(2026, 8, 1, 8, 59, 0, DateTimeKind.Utc),
                type = LedgerTransactionType.Spend,
                amount = -30,
                reasonCode = "project.complete",
                relatedEntityId = "project.first",
                resultingBalance = 70,
            });
            return profile;
        }

        private static PlayerProfile BuildDurableBaseline()
        {
            var clock = new MutableClock(new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc));
            var profile = new PlayerProfile { schemaVersion = SaveSchemaVersions.Current, createdAtUtc = clock.UtcNow };
            var ledger = new VitalityLedger(profile, clock, new DomainEvents(), Log.Disabled);
            ledger.Credit(VitalityCredit.Steps(10000));

            var region = profile.worldState.GetOrCreateRegionState("region.ashfall");
            var pump = region.GetOrCreateBuildingState("building.pump", "building.pump.def");
            pump.lifecycleState = BuildingLifecycleState.Restored;
            pump.placement.gridX = 9;
            pump.placement.rotationQuarterTurns = 2;
            pump.placement.placementVersion = 5;
            region.producerStates["producer.pump"] = new ProducerState
            {
                producerId = "producer.pump",
                buildingInstanceId = "building.pump",
                lastCheckpointUtc = clock.UtcNow,
                storedOutput = 3,
            };
            region.completedProjectIds.Add("project.first");
            Assert.IsTrue(profile.activityState.creditedIntervals.TryMarkCredited(
                "activity.debug:window-a"));
            profile.activityState.androidLastRawStepCounter = 999.75;
            return profile;
        }

        private sealed class SingleOperationFaultFileSystem : ISaveFileSystem
        {
            private readonly string _operation;

            public SingleOperationFaultFileSystem(string operation)
            {
                _operation = operation;
            }

            public void EnsureDirectory(string directory)
            {
                Directory.CreateDirectory(directory);
            }

            public bool Exists(string path)
            {
                return File.Exists(path);
            }

            public string ReadAllText(string path)
            {
                return File.ReadAllText(path);
            }

            public void WriteAllText(string path, string contents)
            {
                ThrowIf("write");
                File.WriteAllText(path, contents);
            }

            public void Copy(string sourceFileName, string destFileName, bool overwrite)
            {
                ThrowIf("copy");
                File.Copy(sourceFileName, destFileName, overwrite);
            }

            public void Delete(string path)
            {
                File.Delete(path);
            }

            public void Move(string sourceFileName, string destFileName)
            {
                ThrowIf("move");
                File.Move(sourceFileName, destFileName);
            }

            private void ThrowIf(string operation)
            {
                if (string.Equals(_operation, operation, StringComparison.Ordinal))
                {
                    throw new IOException($"Injected {operation} failure.");
                }
            }
        }
    }
}
