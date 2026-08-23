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
            return new FileSaveRepository(_directory, "profile.json", _serializer, _migrator, Log.Disabled);
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
    }
}
