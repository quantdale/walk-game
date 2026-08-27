using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using WalkGame.Core;
using WalkGame.Persistence;

namespace WalkGame.Tests
{
    /// <summary>
    /// M8.8 H2: SaveMigrator contract — success implies Current, unsupported lower
    /// schemas fail closed, forward schemas fail, and missing migration paths do not
    /// silently coerce. Also proves repository load policy remains fail-closed.
    /// </summary>
    public sealed class M88SaveMigratorContractTests
    {
        private SaveMigrator _migrator;

        [SetUp]
        public void SetUp()
        {
            _migrator = new SaveMigrator();
        }

        [Test]
        public void CurrentSchema_Succeeds_AndVersionIsCurrent()
        {
            var profile = new PlayerProfile { schemaVersion = SaveSchemaVersions.Current };
            bool ok = _migrator.TryMigrateToCurrent(profile, out string error);
            Assert.IsTrue(ok, $"should succeed: {error}");
            Assert.IsNull(error);
            Assert.AreEqual(SaveSchemaVersions.Current, profile.schemaVersion, "success must imply Current");
        }

        [Test]
        public void NullProfile_Fails()
        {
            bool ok = _migrator.TryMigrateToCurrent(null, out string error);
            Assert.IsFalse(ok);
            Assert.IsNotNull(error);
            StringAssert.Contains("null", error.ToLowerInvariant());
        }

        [Test]
        public void ForwardSchema_Fails_AndDoesNotMutate()
        {
            int future = SaveSchemaVersions.Current + 5;
            var profile = new PlayerProfile { schemaVersion = future };
            bool ok = _migrator.TryMigrateToCurrent(profile, out string error);
            Assert.IsFalse(ok);
            Assert.IsNotNull(error);
            StringAssert.Contains("newer", error.ToLowerInvariant());
            Assert.AreEqual(future, profile.schemaVersion, "failed forward migration must not mutate version");
        }

        [Test]
        public void ZeroSchema_FailsClosed_DoesNotCoerceToCurrent()
        {
            var profile = new PlayerProfile { schemaVersion = 0 };
            bool ok = _migrator.TryMigrateToCurrent(profile, out string error);
            Assert.IsFalse(ok, "schema 0 should fail when MinimumSupported=1");
            Assert.IsNotNull(error);
            // Must mention unsupported/minimum, not just generic.
            StringAssert.Contains("minimum", error.ToLowerInvariant());
            Assert.AreEqual(0, profile.schemaVersion, "unsupported schema must not be coerced to Current");
        }

        [Test]
        public void NegativeSchema_FailsClosed()
        {
            var profile = new PlayerProfile { schemaVersion = -1 };
            bool ok = _migrator.TryMigrateToCurrent(profile, out string error);
            Assert.IsFalse(ok);
            Assert.IsNotNull(error);
            StringAssert.Contains("minimum", error.ToLowerInvariant());
            Assert.AreEqual(-1, profile.schemaVersion);
        }

        [Test]
        public void SuccessImpliesCurrent_ForAllSupported()
        {
            // Only Current is supported today; future migrations must preserve this.
            for (int v = SaveSchemaVersions.MinimumSupported; v <= SaveSchemaVersions.Current; v++)
            {
                var profile = new PlayerProfile { schemaVersion = v };
                bool ok = _migrator.TryMigrateToCurrent(profile, out string error);
                if (ok)
                {
                    Assert.AreEqual(SaveSchemaVersions.Current, profile.schemaVersion,
                        $"success with input {v} must yield Current");
                }
            }
        }

        [Test]
        public void MissingMigrationPath_FailsWithoutLooping()
        {
            var profile = new PlayerProfile { schemaVersion = 1 };
            var start = DateTime.UtcNow;
            var migrator = new SaveMigrator(3, 1, new Dictionary<int, Action<PlayerProfile>>());
            bool ok = migrator.TryMigrateToCurrent(profile, out string error);
            var elapsed = DateTime.UtcNow - start;
            Assert.IsFalse(ok);
            Assert.Less(elapsed.TotalSeconds, 1.0, "migrator must fail fast, not loop");
            Assert.IsNotNull(error);
            StringAssert.Contains("No migration path", error);
            Assert.AreEqual(1, profile.schemaVersion);
        }

        [Test]
        public void ZeroSchema_DoesNotReachCurrent_AfterFailedMigration()
        {
            var profile = new PlayerProfile { schemaVersion = 0, vitalityBalance = 123, resources = new System.Collections.Generic.Dictionary<string, long> { { "wood", 5 } } };
            bool ok = _migrator.TryMigrateToCurrent(profile, out _);
            Assert.IsFalse(ok);
            Assert.AreNotEqual(SaveSchemaVersions.Current, profile.schemaVersion);
            // Ensure no progression minted via migration failure.
            Assert.AreEqual(123, profile.vitalityBalance);
            Assert.AreEqual(5, profile.resources["wood"]);
        }

        // --- Repository integration: lower schema material fails closed ---

        [Test]
        public void Repository_Load_WithZeroSchema_FailsClosed_Incompatible()
        {
            string dir = Path.Combine(Path.GetTempPath(), "walkgame-m88-migrator-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var serializer = new JsonSaveSerializer();
                var migrator = new SaveMigrator();
                var clock = new MutableClock(new DateTime(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc));
                var repo = new FileSaveRepository(dir, "profile.json", serializer, migrator, Log.Disabled, clock);

                var zeroProfile = new PlayerProfile { schemaVersion = 0, createdAtUtc = clock.UtcNow };
                string payload = serializer.Serialize(zeroProfile);
                File.WriteAllText(Path.Combine(dir, "profile.json"), payload);

                bool loaded = repo.TryLoad(out var outProfile, out var result);
                Assert.IsFalse(loaded, "zero-schema save should not load");
                Assert.AreEqual(SaveLoadResult.IncompatibleSchema, result);
                Assert.IsNull(outProfile);
                // Ensure no new profile was fabricated on disk.
                // Main file should remain present (failed load does not delete).
                Assert.IsTrue(File.Exists(Path.Combine(dir, "profile.json")));
                string remaining = File.ReadAllText(Path.Combine(dir, "profile.json"));
                var reparsed = Newtonsoft.Json.JsonConvert.DeserializeObject<PlayerProfile>(remaining);
                Assert.AreEqual(0, reparsed.schemaVersion);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Test]
        public void Repository_Load_WithNegativeSchema_FailsClosed()
        {
            string dir = Path.Combine(Path.GetTempPath(), "walkgame-m88-migrator-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var serializer = new JsonSaveSerializer();
                var migrator = new SaveMigrator();
                var clock = new MutableClock(DateTime.UtcNow);
                var repo = new FileSaveRepository(dir, "profile.json", serializer, migrator, Log.Disabled, clock);

                var negProfile = new PlayerProfile { schemaVersion = -5 };
                string payload = serializer.Serialize(negProfile);
                File.WriteAllText(Path.Combine(dir, "profile.json"), payload);

                bool loaded = repo.TryLoad(out _, out var result);
                Assert.IsFalse(loaded);
                Assert.AreEqual(SaveLoadResult.IncompatibleSchema, result);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Test]
        public void Repository_Load_WithCurrentSchema_Succeeds()
        {
            string dir = Path.Combine(Path.GetTempPath(), "walkgame-m88-migrator-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var serializer = new JsonSaveSerializer();
                var migrator = new SaveMigrator();
                var clock = new MutableClock(new DateTime(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc));
                var repo = new FileSaveRepository(dir, "profile.json", serializer, migrator, Log.Disabled, clock);

                var current = new PlayerProfile { schemaVersion = SaveSchemaVersions.Current, vitalityBalance = 42 };
                string payload = serializer.Serialize(current);
                File.WriteAllText(Path.Combine(dir, "profile.json"), payload);

                bool loaded = repo.TryLoad(out var outProfile, out var result);
                Assert.IsTrue(loaded);
                Assert.AreEqual(SaveLoadResult.Success, result);
                Assert.IsNotNull(outProfile);
                Assert.AreEqual(SaveSchemaVersions.Current, outProfile.schemaVersion);
                Assert.AreEqual(42, outProfile.vitalityBalance);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Test]
        public void MigrationStep_NoProgress_FailsClosed()
        {
            var profile = new PlayerProfile { schemaVersion = 1 };
            var migrator = new SaveMigrator(3, 1, new Dictionary<int, Action<PlayerProfile>>
            {
                { 1, _ => { } },
            });

            bool ok = migrator.TryMigrateToCurrent(profile, out string error);

            Assert.IsFalse(ok);
            StringAssert.Contains("failed to advance", error);
            Assert.AreEqual(1, profile.schemaVersion);
        }

        [Test]
        public void MigrationStep_BackwardProgress_FailsClosed()
        {
            var profile = new PlayerProfile { schemaVersion = 2 };
            var migrator = new SaveMigrator(3, 1, new Dictionary<int, Action<PlayerProfile>>
            {
                { 2, p => p.schemaVersion = 1 },
            });

            bool ok = migrator.TryMigrateToCurrent(profile, out string error);

            Assert.IsFalse(ok);
            StringAssert.Contains("failed to advance", error);
            Assert.AreEqual(1, profile.schemaVersion);
        }

        [Test]
        public void MigrationStep_JumpProgress_FailsClosed()
        {
            var profile = new PlayerProfile { schemaVersion = 1 };
            var migrator = new SaveMigrator(3, 1, new Dictionary<int, Action<PlayerProfile>>
            {
                { 1, p => p.schemaVersion = 3 },
            });

            bool ok = migrator.TryMigrateToCurrent(profile, out string error);

            Assert.IsFalse(ok);
            StringAssert.Contains("failed to advance", error);
            Assert.AreEqual(3, profile.schemaVersion);
        }

        [Test]
        public void MigrationSteps_AdvanceSequentially_ToExactCurrent()
        {
            var profile = new PlayerProfile { schemaVersion = 1 };
            var migrator = new SaveMigrator(3, 1, new Dictionary<int, Action<PlayerProfile>>
            {
                { 1, p => p.schemaVersion = 2 },
                { 2, p => p.schemaVersion = 3 },
            });

            bool ok = migrator.TryMigrateToCurrent(profile, out string error);

            Assert.IsTrue(ok, error);
            Assert.IsNull(error);
            Assert.AreEqual(3, profile.schemaVersion);
        }
    }
}
