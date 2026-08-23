using System;
using System.IO;
using NUnit.Framework;
using WalkGame.Core;
using WalkGame.Gameplay;
using WalkGame.Persistence;

namespace WalkGame.Tests
{
    public sealed class SaveLoadTests
    {
        private string _directory;
        private MutableClock _clock;
        private JsonSaveSerializer _serializer;
        private SaveMigrator _migrator;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "walkgame-tests", Guid.NewGuid().ToString("N"));
            _clock = new MutableClock(new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc));
            _serializer = new JsonSaveSerializer();
            _migrator = new SaveMigrator();
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        private FileSaveRepository CreateRepository()
        {
            return CreateRepository(null, Log.Disabled);
        }

        private FileSaveRepository CreateRepository(ISaveFileSystem fileSystem, Log log)
        {
            return new FileSaveRepository(_directory, "profile.json", _serializer, _migrator, log, _clock, fileSystem);
        }

        [Test]
        public void EmptyRepository_ReportsEmpty()
        {
            var repository = CreateRepository();
            bool loaded = repository.TryLoad(out _, out var result);

            Assert.IsFalse(loaded);
            Assert.AreEqual(SaveLoadResult.Empty, result);
        }

        [Test]
        public void SaveThenLoad_PreservesCanonicalState()
        {
            var profile = BuildPopulatedProfile(out var expectedPlacement);
            var repository = CreateRepository();

            Assert.AreEqual(SaveLoadResult.Success, repository.Save(profile));

            bool loaded = repository.TryLoad(out var restored, out var result);
            Assert.IsTrue(loaded);
            Assert.AreEqual(SaveLoadResult.Success, result);

            Assert.AreEqual(profile.vitalityBalance, restored.vitalityBalance);
            Assert.AreEqual(profile.lifetimeAcceptedSteps, restored.lifetimeAcceptedSteps);
            Assert.AreEqual(expectedPlacement.gridX,
                restored.worldState.regionStates[TestContent.RegionId]
                    .buildingStates[TestContent.PumpInstanceId].placement.gridX);
            Assert.AreEqual(BuildingLifecycleState.Restored,
                restored.worldState.regionStates[TestContent.RegionId]
                    .buildingStates[TestContent.PumpInstanceId].lifecycleState);
            Assert.AreEqual(1, restored.schemaVersion);
            Assert.AreEqual(_clock.UtcNow, restored.lastSavedAtUtc);
            Assert.AreEqual(DateTimeKind.Utc, restored.lastSavedAtUtc.Kind);
        }

        [Test]
        public void SaveReload_NormalizesPersistedTimestampsToUtc()
        {
            var profile = new PlayerProfile
            {
                // An explicitly unspecified value models a serialized/local-time
                // boundary; the save contract treats all lifecycle timestamps as UTC.
                createdAtUtc = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Unspecified),
            };
            var repository = CreateRepository();

            Assert.AreEqual(SaveLoadResult.Success, repository.Save(profile));
            Assert.IsTrue(repository.TryLoad(out var restored, out var result));
            Assert.AreEqual(SaveLoadResult.Success, result);
            Assert.AreEqual(DateTimeKind.Utc, restored.createdAtUtc.Kind);
            Assert.AreEqual(new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc),
                restored.createdAtUtc);
        }

        [Test]
        public void CorruptMainSave_RecoversFromBackup()
        {
            var repository = CreateRepository();
            repository.Save(BuildPopulatedProfile(out _));
            Assert.IsTrue(repository.BackupExists(), "second save must rotate a backup");

            // Corrupt the main file but keep the backup.
            File.WriteAllText(Path.Combine(_directory, "profile.json"), "{ not json !!!");

            bool loaded = repository.TryLoad(out var restored, out var result);
            Assert.IsTrue(loaded);
            Assert.AreEqual(SaveLoadResult.RecoveredFromBackup, result);
            Assert.IsNotNull(restored);
        }

        [Test]
        public void InterruptedWrite_MissingMainSave_RecoversFromBackup()
        {
            // Crash-equivalent of a failed atomic replace: rotation removed the main
            // file but the process died before the temp move completed.
            var repository = CreateRepository();
            repository.Save(BuildPopulatedProfile(out _));
            File.Delete(Path.Combine(_directory, "profile.json"));

            bool loaded = repository.TryLoad(out _, out var result);

            Assert.IsTrue(loaded);
            Assert.AreEqual(SaveLoadResult.RecoveredFromBackup, result);
        }

        [Test]
        public void FutureTimestamp_IsFlagged_AndDoesNotGrantFutureProduction()
        {
            var profile = BuildPopulatedProfile(out _);
            profile.worldState.regionStates[TestContent.RegionId]
                .buildingStates[TestContent.PumpInstanceId].restorationCompletedAtUtc =
                _clock.UtcNow.AddDays(30); // corrupted/future timestamp

            var sink = new RecordingLog();
            var report = SaveValidator.RepairAndValidate(
                profile,
                _clock,
                new Log(sink, LogLevel.Warning));

            Assert.IsTrue(report.HasAnomalies);
            Assert.AreEqual(1, report.FutureRestorationTimestampCount);
            StringAssert.Contains("Future restoration timestamp", sink.Messages[0]);

            // A future checkpoint is an anomaly, never a source of free production.
            var region = profile.worldState.regionStates[TestContent.RegionId];
            region.producerStates[TestContent.PumpProducerId] = new ProducerState
            {
                producerId = TestContent.PumpProducerId,
                buildingInstanceId = TestContent.PumpInstanceId,
                lastCheckpointUtc = _clock.UtcNow.AddHours(4),
            };
            var rewards = new RewardApplier(profile, _clock, new DomainEvents(), Log.Disabled);
            var testCatalog = TestContent.Create();
            testCatalog.Index();
            var production = new ProductionService(testCatalog, profile, rewards, _clock, Log.Disabled);
            var productionResult = production.Accrue(TestContent.RegionId,
                region.producerStates[TestContent.PumpProducerId]);
            Assert.IsTrue(productionResult.clockAnomaly);
            Assert.AreEqual(0, productionResult.produced);
            Assert.AreEqual(0, region.producerStates[TestContent.PumpProducerId].storedOutput);

            // Validator flags rather than trusts the timestamp, and recovery preserves
            // the record for reconciliation instead of silently wiping it.
            var repository = CreateRepository();
            Assert.AreEqual(SaveLoadResult.Success, repository.Save(profile));
            bool loaded = repository.TryLoad(out var restored, out _);
            Assert.IsTrue(loaded);
            Assert.IsNotNull(restored.worldState.regionStates[TestContent.RegionId]
                .buildingStates[TestContent.PumpInstanceId].restorationCompletedAtUtc);
        }

        [Test]
        public void NewerSchema_IsRejected_NotWiped()
        {
            var repository = CreateRepository();
            var futureProfile = new PlayerProfile { schemaVersion = SaveSchemaVersions.Current + 5 };
            string payload = _serializer.Serialize(futureProfile);
            File.WriteAllText(Path.Combine(_directory, "profile.json"), payload);

            bool loaded = repository.TryLoad(out _, out var result);

            Assert.IsFalse(loaded);
            Assert.AreEqual(SaveLoadResult.IncompatibleSchema, result);
            Assert.IsTrue(repository.MainSaveExists(), "incompatible save must never be deleted");
        }

        [Test]
        public void CorruptMainAndBackup_ReportsFailure_WithoutWipingEitherFile()
        {
            var repository = CreateRepository();
            repository.Save(BuildPopulatedProfile(out _));
            File.WriteAllText(Path.Combine(_directory, "profile.json"), "{ broken main");
            File.WriteAllText(Path.Combine(_directory, "profile.json.bak"), "{ broken backup");

            bool loaded = repository.TryLoad(out _, out var result);

            Assert.IsFalse(loaded);
            Assert.AreEqual(SaveLoadResult.Failed, result);
            Assert.IsTrue(repository.MainSaveExists());
            Assert.IsTrue(repository.BackupExists());
        }

        [Test]
        public void WriteFailureBeforeTempCompletion_PreservesLastKnownGoodMain()
        {
            var original = BuildPopulatedProfile(out _);
            var repository = CreateRepository();
            Assert.AreEqual(SaveLoadResult.Success, repository.Save(original));

            var failingFileSystem = new FaultInjectingSaveFileSystem { FailOperation = "write" };
            var failingRepository = CreateRepository(failingFileSystem, Log.Disabled);
            var attempted = BuildPopulatedProfile(out _);
            attempted.vitalityBalance += 999;

            Assert.AreEqual(SaveLoadResult.Failed, failingRepository.Save(attempted));
            Assert.IsFalse(File.Exists(Path.Combine(_directory, "profile.json.tmp")));

            bool loaded = repository.TryLoad(out var restored, out var result);
            Assert.IsTrue(loaded);
            Assert.AreEqual(SaveLoadResult.Success, result);
            Assert.AreEqual(original.vitalityBalance, restored.vitalityBalance);
        }

        [Test]
        public void FailureAfterTempCreation_PreservesLastKnownGoodMain()
        {
            var repository = CreateRepository();
            var original = BuildPopulatedProfile(out _);
            Assert.AreEqual(SaveLoadResult.Success, repository.Save(original));

            var failingFileSystem = new FaultInjectingSaveFileSystem { FailOperation = "copy" };
            var failingRepository = CreateRepository(failingFileSystem, Log.Disabled);
            Assert.AreEqual(SaveLoadResult.Failed, failingRepository.Save(BuildPopulatedProfile(out _)));

            bool loaded = repository.TryLoad(out var restored, out var result);
            Assert.IsTrue(loaded);
            Assert.AreEqual(SaveLoadResult.Success, result);
            Assert.AreEqual(original.vitalityBalance, restored.vitalityBalance);
            Assert.IsFalse(File.Exists(Path.Combine(_directory, "profile.json.tmp")));
        }

        [Test]
        public void FailureDuringBackupRotation_LeavesValidBackupForRecovery()
        {
            var repository = CreateRepository();
            var original = BuildPopulatedProfile(out _);
            Assert.AreEqual(SaveLoadResult.Success, repository.Save(original));

            var failingFileSystem = new FaultInjectingSaveFileSystem { ThrowAfterDeletingMain = true };
            var failingRepository = CreateRepository(failingFileSystem, Log.Disabled);
            Assert.AreEqual(SaveLoadResult.Failed, failingRepository.Save(BuildPopulatedProfile(out _)));

            bool loaded = repository.TryLoad(out var restored, out var result);
            Assert.IsTrue(loaded);
            Assert.AreEqual(SaveLoadResult.RecoveredFromBackup, result);
            Assert.AreEqual(original.vitalityBalance, restored.vitalityBalance);
        }

        [Test]
        public void Validator_ClampsImpossibleState()
        {
            var profile = new PlayerProfile
            {
                vitalityBalance = -100,
                lifetimeAcceptedSteps = -50,
            };
            profile.resources[WellKnownIds.Resources.Biomass] = -7;

            SaveValidator.RepairAndValidate(profile, Log.Disabled);

            Assert.AreEqual(0, profile.vitalityBalance);
            Assert.AreEqual(0, profile.lifetimeAcceptedSteps);
            Assert.AreEqual(0, profile.resources[WellKnownIds.Resources.Biomass]);
        }

        [Test]
        public void RoundTrip_SurvivesManyMutations()
        {
            var repository = CreateRepository();
            var profile = new PlayerProfile();

            for (int i = 1; i <= 25; i++)
            {
                profile.vitalityBalance += 10;
                repository.Save(profile);
                bool loaded = repository.TryLoad(out var restored, out var result);
                Assert.IsTrue(loaded, $"iteration {i}: {result}");
                Assert.AreEqual(i * 10, restored.vitalityBalance);
                profile = restored;
            }
        }

        [Test]
        public void SaveReload_PreservesExactlyOnceDedupState_AfterActivityCredit()
        {
            // Campaign S16: activity credit -> save -> restart must keep dedup keys so a
            // replayed snapshot/session cannot pay twice across process boundaries.
            var profile = new PlayerProfile();
            profile.activityState.lastSuccessfulSyncUtc = _clock.UtcNow.AddMinutes(-30);
            Assert.IsTrue(profile.activityState.creditedIntervals.TryMarkCredited(
                "activity.ios.coremotion:2026-06-01T09:30:00.0000000Z:2026-06-01T10:00:00.0000000Z"));
            Assert.IsTrue(profile.activityState.creditedSessionIds.TryMarkCredited("session:abc-123"));
            profile.activityState.androidLastRawStepCounter = 12345.0;
            profile.lifetimeAcceptedSteps = 6000;
            profile.lifetimeVerifiedDistanceMeters = 4200;

            var repository = CreateRepository();
            Assert.AreEqual(SaveLoadResult.Success, repository.Save(profile));

            bool loaded = repository.TryLoad(out var restored, out var result);
            Assert.IsTrue(loaded);
            Assert.AreEqual(SaveLoadResult.Success, result);

            string replayedIntervalKey = "activity.ios.coremotion:2026-06-01T09:30:00.0000000Z:2026-06-01T10:00:00.0000000Z";
            Assert.IsTrue(restored.activityState.creditedIntervals.Contains(replayedIntervalKey),
                "credited intervals must survive restart");
            Assert.IsFalse(restored.activityState.creditedIntervals.TryMarkCredited(replayedIntervalKey));
            Assert.IsTrue(restored.activityState.creditedSessionIds.Contains("session:abc-123"));
            Assert.IsFalse(restored.activityState.creditedSessionIds.TryMarkCredited("session:abc-123"));
            Assert.AreEqual(12345.0, restored.activityState.androidLastRawStepCounter.GetValueOrDefault());
        }

        [Test]
        public void Validator_RepairsNullDedupStores_FromOldSaves()
        {
            var payload = _serializer.Serialize(new PlayerProfile());
            payload = payload.Replace("\"creditedSessionIds\"", "\"ignoredField\"");
            var parsed = _serializer.Deserialize(payload);
            // Simulate a hand-edited save with explicit nulls:
            parsed.activityState.creditedIntervals = null;
            parsed.activityState.creditedSessionIds = null;

            SaveValidator.RepairAndValidate(parsed, Log.Disabled);

            Assert.IsNotNull(parsed.activityState.creditedIntervals);
            Assert.IsNotNull(parsed.activityState.creditedSessionIds);
        }

        private PlayerProfile BuildPopulatedProfile(out BuildingPlacement expectedPlacement)
        {
            var catalog = TestContent.Create();
            catalog.Index();

            var profile = new PlayerProfile();
            var ledger = new VitalityLedger(profile, _clock, new DomainEvents(), Log.Disabled);
            ledger.Credit(VitalityCredit.Steps(4200));

            var region = profile.worldState.GetOrCreateRegionState(TestContent.RegionId);
            foreach (var instance in catalog.GetRegion(TestContent.RegionId).defaultBuildingInstances)
            {
                var state = region.GetOrCreateBuildingState(instance.instanceId, instance.buildingDefinitionId);
                state.placement.gridX = instance.initialPlacement.gridX + 2;
                state.placement.gridY = instance.initialPlacement.gridY;
                state.lifecycleState = BuildingLifecycleState.Restored;
            }

            region.completedProjectIds.Add("project.test.clear_rubble");
            region.discoveredLoreIds.Add("lore.test.first");

            expectedPlacement = region.buildingStates[TestContent.PumpInstanceId].placement;
            return profile;
        }

        private sealed class RecordingLog : ILog
        {
            public readonly System.Collections.Generic.List<string> Messages =
                new System.Collections.Generic.List<string>();

            public void Log(LogLevel level, string message)
            {
                Messages.Add(message);
            }
        }

        private sealed class FaultInjectingSaveFileSystem : ISaveFileSystem
        {
            public string FailOperation { get; set; }
            public bool ThrowAfterDeletingMain { get; set; }

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
                if (ThrowAfterDeletingMain && string.Equals(
                    Path.GetFileName(path), "profile.json", StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException("Injected interruption after main-file deletion.");
                }
            }

            public void Move(string sourceFileName, string destFileName)
            {
                ThrowIf("move");
                File.Move(sourceFileName, destFileName);
            }

            private void ThrowIf(string operation)
            {
                if (string.Equals(FailOperation, operation, StringComparison.Ordinal))
                {
                    throw new IOException($"Injected {operation} failure.");
                }
            }
        }
    }
}
