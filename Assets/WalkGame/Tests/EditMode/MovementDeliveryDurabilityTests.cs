using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using WalkGame.Activity;
using WalkGame.Core;
using WalkGame.Persistence;

namespace WalkGame.Tests
{
    /// <summary>
    /// M8.3 movement-delivery durability contract (ADR 0009): a prepared passive
    /// delivery or completed Expedition result must never be irreversibly consumed
    /// before its profile commit outcome is known. A transient save failure followed
    /// by retry must neither permanently lose observed base movement nor credit it
    /// twice, and every resolution must be idempotent against stale/repeated calls.
    /// </summary>
    public sealed class MovementDeliveryDurabilityTests
    {
        private string _directory;
        private MutableClock _clock;
        private PlayerProfile _profile;
        private DomainEvents _events;
        private VitalityLedger _ledger;
        private DebugActivityProvider _provider;
        private ActivityService _activity;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "walkgame-tests", Guid.NewGuid().ToString("N"));
            _clock = new MutableClock(new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc));
            _profile = new PlayerProfile();
            _events = new DomainEvents();
            _ledger = new VitalityLedger(_profile, _clock, _events, Log.Disabled);
            _provider = new DebugActivityProvider(_clock);
            // The fake provider boots with 5,000 pre-accumulated steps; zero it so
            // every scenario reasons about exact step counts.
            _provider.DebugSimulateReboot();
            _activity = CreateActivityService(_profile);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        private ActivityService CreateActivityService(PlayerProfile profile)
        {
            return new ActivityService(
                profile,
                _ledger,
                new TrustEvaluator(RewardPolicy.Default),
                new RewardCalculator(RewardPolicy.Default),
                _events,
                Log.Disabled);
        }

        private PreparedActivityDelivery PreparePassive()
        {
            return _provider.PreparePassiveDeliveryAsync(new ActivityCursor()).GetAwaiter().GetResult();
        }

        private PassiveReconciliationResult Process(PreparedActivityDelivery delivery)
        {
            return _activity.ProcessPassiveSnapshot(delivery.snapshot);
        }

        // ---- G1: prepared -> commit succeeds -> acknowledge -> exactly once ----

        [Test]
        public void PreparedDelivery_CommitSucceeds_Acknowledged_CreditsExactlyOnce()
        {
            _provider.DebugAddSteps(800);

            var delivery = PreparePassive();
            Assert.IsNotNull(delivery, "staged movement must be deliverable");
            Assert.AreEqual(800, delivery.snapshot.stepCount);

            var outcome = Process(delivery);
            Assert.IsTrue(outcome.RequiresCommit);
            Assert.AreEqual(800, outcome.acceptedSteps);

            // The profile commit succeeded: acknowledgment drops the staging.
            _provider.ResolvePreparedDelivery(delivery, durable: true);

            var next = PreparePassive();
            Assert.IsNull(next?.snapshot, "acknowledged movement must never be re-delivered");
            Assert.AreEqual(800, _ledger.GetBalance());
            Assert.AreEqual(800, _profile.lifetimeAcceptedSteps);
        }

        // ---- G2/G7: prepared -> real commit failure -> reject -> retry ----------

        [Test]
        public void TransientSaveFailure_RejectsDelivery_RetryCreditsExactlyOnce()
        {
            var clean = new FileSaveRepository(
                _directory, "profile.json", new JsonSaveSerializer(), new SaveMigrator(), Log.Disabled, _clock);
            var failing = new FileSaveRepository(
                _directory, "profile.json", new JsonSaveSerializer(), new SaveMigrator(),
                Log.Disabled, _clock, new WriteFaultFileSystem());

            Assert.AreEqual(SaveLoadResult.Success, clean.Save(_profile));

            // A passive window arrives and is prepared; the domain mutates the profile.
            _provider.DebugAddSteps(600);
            var delivery = PreparePassive();
            var outcome = Process(delivery);
            Assert.AreEqual(600, outcome.acceptedSteps);

            // The save fails; the coordinator reverts the live graph to disk truth.
            Assert.AreEqual(
                PersistenceCommitOutcome.RevertedToLastKnownGood,
                new PersistenceCoordinator(failing, Log.Disabled, null).Commit(_profile));
            Assert.AreEqual(0, _ledger.GetBalance(), "the rollback removed the phantom credit");

            // The ticker resolves the delivery against the failed outcome.
            _provider.ResolvePreparedDelivery(delivery, durable: false);

            // The retry delivers the SAME base movement onto the reverted profile.
            var retry = PreparePassive();
            Assert.IsNotNull(retry?.snapshot, "rejected base movement must stay retryable");
            Assert.AreEqual(600, retry.snapshot.stepCount);

            var retryOutcome = Process(retry);
            Assert.AreEqual(600, retryOutcome.acceptedSteps);
            Assert.AreEqual(SaveLoadResult.Success, clean.Save(_profile)); // the retry commits
            _provider.ResolvePreparedDelivery(retry, durable: true);

            Assert.IsTrue(clean.TryLoad(out var durable, out _));
            Assert.AreEqual(600, durable.vitalityBalance, "credited exactly once across both attempts");
            Assert.AreEqual(600, durable.lifetimeAcceptedSteps);
        }

        [Test]
        public void FailedCommit_DoesNotLoseTheFakePassiveCounter()
        {
            _provider.DebugAddSteps(4321);
            var delivery = PreparePassive();

            _provider.ResolvePreparedDelivery(delivery, durable: false);

            var retry = PreparePassive();
            Assert.AreEqual(4321, retry.snapshot.stepCount,
                "a rejected delivery must restore the staged fake counter");

            var retryOutcome = _activity.ProcessPassiveSnapshot(retry.snapshot);
            Assert.AreEqual(4321, retryOutcome.acceptedSteps);
            _provider.ResolvePreparedDelivery(retry, durable: true);
            Assert.AreEqual(4321, _profile.lifetimeAcceptedSteps);
        }

        // ---- G3: repeated/stale resolves are idempotent --------------------------

        [Test]
        public void RepeatedResolves_NeverDuplicateCredit_OrGoNegative()
        {
            _provider.DebugAddSteps(500);
            var delivery = PreparePassive();

            Process(delivery);
            _provider.ResolvePreparedDelivery(delivery, durable: true);
            _provider.ResolvePreparedDelivery(delivery, durable: true);   // repeated ack
            _provider.ResolvePreparedDelivery(delivery, durable: false);  // stale reject
            _provider.ResolvePreparedDelivery(null, durable: false);      // null-safe

            var next = PreparePassive();
            Assert.IsNull(next?.snapshot, "resolved movement never replays");
            Assert.AreEqual(500, _ledger.GetBalance());
            Assert.AreEqual(500, _profile.lifetimeAcceptedSteps);
        }

        [Test]
        public void RejectThenAcknowledge_ResolvesExactlyOnce()
        {
            _provider.DebugAddSteps(300);
            var delivery = PreparePassive();

            _provider.ResolvePreparedDelivery(delivery, durable: false);
            _provider.ResolvePreparedDelivery(delivery, durable: true); // stale: must not drop restored steps

            var retry = PreparePassive();
            Assert.IsNotNull(retry?.snapshot);
            Assert.AreEqual(300, retry.snapshot.stepCount, "the restored movement survived the stale ack");
        }

        // ---- G6: overlapping reads cannot double-claim one window ---------------

        [Test]
        public void OverlappingPreparation_CannotOpenTwoClaimsOverOneWindow()
        {
            _provider.DebugAddSteps(250);
            var first = PreparePassive();
            var second = PreparePassive();

            Assert.IsNotNull(first);
            Assert.IsNull(second, "one open claim owns the staged window");

            _provider.ResolvePreparedDelivery(first, durable: true);
            Assert.IsNull(PreparePassive()?.snapshot, "nothing remains after acknowledgment");
        }

        // ---- G8: proven-durable duplicates need no write and never replay -------

        [Test]
        public void DuplicateDurableDelivery_NeedsNoSave_AndAcknowledgesWithoutReplay()
        {
            _provider.DebugAddSteps(700);
            var delivery = PreparePassive();
            Process(delivery);
            _provider.ResolvePreparedDelivery(delivery, durable: true); // committed

            // Re-deliver the identical interval: durable state already proves it
            // consumed, so no profile write is required and nothing replays.
            var duplicateOutcome = _activity.ProcessPassiveSnapshot(delivery.snapshot);
            Assert.AreEqual(PassiveReconciliationDisposition.DuplicateDurable, duplicateOutcome.disposition);
            Assert.IsFalse(duplicateOutcome.RequiresCommit, "durable state already proves consumption");
            Assert.IsNull(PreparePassive()?.snapshot);
            Assert.AreEqual(700, _ledger.GetBalance());
        }

        [Test]
        public void CursorOnlyDuplicateMutation_StillRequiresPersistence()
        {
            _provider.DebugAddSteps(400);
            var delivery = PreparePassive();
            Process(delivery);
            _provider.ResolvePreparedDelivery(delivery, durable: true);

            // Simulate durable state where the dedup key exists but the stored sync
            // cursor sits behind the credited interval end: re-observing the same
            // interval must repair the cursor durably without changing any reward.
            _profile.activityState.lastSuccessfulSyncUtc = delivery.snapshot.intervalStartUtc;

            var outcome = _activity.ProcessPassiveSnapshot(delivery.snapshot);
            Assert.AreEqual(PassiveReconciliationDisposition.DurableMutation, outcome.disposition);
            Assert.IsTrue(outcome.RequiresCommit,
                "cursor-only repair is a canonical change that must persist");
            Assert.AreEqual(
                delivery.snapshot.intervalEndUtc,
                _profile.activityState.lastSuccessfulSyncUtc.GetValueOrDefault());
            Assert.AreEqual(400, _ledger.GetBalance(), "no reward change for the duplicate");
        }

        // ---- G9: null/no-movement reads never justify a profile write ------------

        [Test]
        public void NullOrEmptyReads_ProduceNoDelivery_NoCommit()
        {
            var outcome = _activity.ProcessPassiveSnapshot(null);
            Assert.AreEqual(PassiveReconciliationDisposition.NoDelivery, outcome.disposition);
            Assert.IsFalse(outcome.RequiresCommit);

            _provider.DebugSimulateReboot(); // empty counter
            Assert.IsNull(PreparePassive()?.snapshot);
        }

        // ---- G11/G12: Expedition completion durability ---------------------------

        [Test]
        public async Task ExpeditionCompletion_CommitFails_BaseMovementRecoversThroughPassiveStream()
        {
            await _provider.StartSessionAsync(SessionType.Walk);
            Assert.IsTrue(_activity.BeginExpedition(SessionType.Walk, _clock.UtcNow));
            _provider.SimulateSessionProgress(additionalSteps: 900, distanceMeters: 0, movingSeconds: 600);

            var result = await _provider.StopSessionAsync();
            Assert.AreEqual(900, result.acceptedSteps);

            // Domain processed the win, then CommitChanges failed and reverted:
            // reject the provider-held completion so the movement is not eaten.
            _activity.AbandonExpedition();
            _provider.ResolveSessionCompletion(result.sessionId, durable: false);

            var recovery = PreparePassive();
            Assert.IsNotNull(recovery?.snapshot, "session base movement must stay recoverable");
            Assert.AreEqual(900, recovery.snapshot.stepCount);

            var outcome = Process(recovery);
            Assert.AreEqual(900, outcome.acceptedSteps);
            _provider.ResolvePreparedDelivery(recovery, durable: true);
            Assert.AreEqual(900, _ledger.GetBalance());

            // The completion claim resolved exactly once: further resolutions of the
            // same session id are safe no-ops and cannot strand or duplicate movement.
            _provider.ResolveSessionCompletion(result.sessionId, durable: false);
            _provider.ResolveSessionCompletion(result.sessionId, durable: true);
            Assert.IsNull(PreparePassive()?.snapshot);
            Assert.AreEqual(900, _ledger.GetBalance(), "exactly once across the recovery");
        }

        [Test]
        public void ExpeditionCompletion_TransientSaveFailure_SameResultReplayRetriesExactlyOnce()
        {
            var clean = new FileSaveRepository(
                _directory, "profile.json", new JsonSaveSerializer(), new SaveMigrator(), Log.Disabled, _clock);
            var failing = new FileSaveRepository(
                _directory, "profile.json", new JsonSaveSerializer(), new SaveMigrator(),
                Log.Disabled, _clock, new WriteFaultFileSystem());

            Assert.AreEqual(SaveLoadResult.Success, clean.Save(_profile));

            _provider.StartSessionAsync(SessionType.Walk).GetAwaiter().GetResult();
            Assert.IsTrue(_activity.BeginExpedition(SessionType.Walk, _clock.UtcNow));
            _provider.SimulateSessionProgress(additionalSteps: 900, distanceMeters: 0, movingSeconds: 600);
            var result = _provider.StopSessionAsync().GetAwaiter().GetResult();

            // First delivery attempt: domain credits, then the save fails and the
            // coordinator reverts the live graph (session id mark included).
            var first = _activity.ProcessSessionResult(result, growthEligible: false);
            Assert.AreEqual(900, first.acceptedSteps);
            Assert.AreEqual(
                PersistenceCommitOutcome.RevertedToLastKnownGood,
                new PersistenceCoordinator(failing, Log.Disabled, null).Commit(_profile));
            Assert.AreEqual(0, _ledger.GetBalance());

            // Same-process retry replays the exact same stable session identity.
            _activity.AbandonExpedition();
            var retry = _activity.ProcessSessionResult(result, growthEligible: false);
            Assert.AreEqual(900, retry.acceptedSteps,
                "the rolled-back dedup mark frees the same result for exactly-one retry");
            Assert.AreEqual(SaveLoadResult.Success, clean.Save(_profile));
            _provider.ResolveSessionCompletion(result.sessionId, durable: true);

            Assert.IsTrue(clean.TryLoad(out var durable, out _));
            Assert.AreEqual(900, durable.vitalityBalance, "base movement survived the failed save");

            // Once durably marked, any further replay is harmless.
            var lateReplay = _activity.ProcessSessionResult(result, growthEligible: false);
            Assert.AreEqual(0, lateReplay.acceptedSteps);
        }

        [Test]
        public async Task ExpeditionCompletion_CommitSucceeds_Acknowledged_NoPassiveReplay()
        {
            await _provider.StartSessionAsync(SessionType.Walk);
            Assert.IsTrue(_activity.BeginExpedition(SessionType.Walk, _clock.UtcNow));
            _provider.SimulateSessionProgress(additionalSteps: 450, distanceMeters: 0, movingSeconds: 300);

            var result = await _provider.StopSessionAsync();
            var processed = _activity.ProcessSessionResult(result, growthEligible: false);
            Assert.AreEqual(450, processed.acceptedSteps);

            _provider.ResolveSessionCompletion(result.sessionId, durable: true);

            var passive = PreparePassive();
            Assert.IsNull(passive?.snapshot, "durably credited session movement never re-enters passive");
            Assert.AreEqual(450, _ledger.GetBalance());
        }

        [Test]
        public void SessionResolution_UnknownOrRepeatedIds_AreSafeNoOps()
        {
            _provider.ResolveSessionCompletion("session.unknown", durable: false);
            _provider.ResolveSessionCompletion(null, durable: true);

            _provider.DebugAddSteps(120);
            var delivery = PreparePassive();
            Assert.IsNotNull(delivery, "unrelated resolutions never disturb passive staging");
            Process(delivery);
            _provider.ResolvePreparedDelivery(delivery, durable: true);
            Assert.AreEqual(120, _profile.lifetimeAcceptedSteps);
        }

        // ---- Suppressed deliveries return to the provider intact (E4) -----------

        [Test]
        public void SuppressedDelivery_RejectedBack_RetryableAfterSession()
        {
            _provider.DebugAddSteps(1000);
            var delivery = PreparePassive();

            // An Expedition claims the domain mid-flight; the ticker rejects the
            // prepared delivery so its movement is held, not consumed.
            _profile.activityState.activeSession = new ActiveSessionState();
            var outcome = Process(delivery);
            Assert.AreEqual(PassiveReconciliationDisposition.SuppressedBySession, outcome.disposition);
            _provider.ResolvePreparedDelivery(delivery, durable: false);

            _profile.activityState.activeSession = null;

            var retry = PreparePassive();
            Assert.IsNotNull(retry?.snapshot);
            Assert.AreEqual(1000, retry.snapshot.stepCount, "suppression must not strand movement");
        }

        /// <summary>Injected write failure so a commit can be proven non-durable.</summary>
        private sealed class WriteFaultFileSystem : ISaveFileSystem
        {
            public void EnsureDirectory(string directory) => Directory.CreateDirectory(directory);
            public bool Exists(string path) => File.Exists(path);
            public string ReadAllText(string path) => File.ReadAllText(path);

            public void WriteAllText(string path, string contents)
            {
                throw new IOException("Injected write failure.");
            }

            public void Copy(string sourceFileName, string destFileName, bool overwrite) =>
                File.Copy(sourceFileName, destFileName, overwrite);

            public void Delete(string path) => File.Delete(path);

            public void Move(string sourceFileName, string destFileName) =>
                File.Move(sourceFileName, destFileName);
        }
    }
}
