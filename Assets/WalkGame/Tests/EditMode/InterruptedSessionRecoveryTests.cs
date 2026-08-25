using System;
using System.IO;
using NUnit.Framework;
using WalkGame.Activity;
using WalkGame.Core;
using WalkGame.Persistence;

namespace WalkGame.Tests
{
    /// <summary>
    /// M8 red-team coverage for the exact-once invariant around interrupted movement
    /// sessions: process death mid-Expedition used to leave a persisted suppression
    /// marker that blocked every future passive reward forever. These tests pin the
    /// boot-time recovery contract and its interaction with dedup keys, cursors,
    /// late-delivered session results, and save/reload.
    /// </summary>
    public sealed class InterruptedSessionRecoveryTests
    {
        private MutableClock _clock;
        private PlayerProfile _profile;
        private DomainEvents _events;
        private VitalityLedger _ledger;
        private DebugActivityProvider _provider;
        private ActivityService _activity;

        [SetUp]
        public void SetUp()
        {
            _clock = new MutableClock(new DateTime(2026, 8, 10, 9, 0, 0, DateTimeKind.Utc));
            _profile = new PlayerProfile();
            _events = new DomainEvents();
            _ledger = new VitalityLedger(_profile, _clock, _events, Log.Disabled);
            _provider = new DebugActivityProvider(_clock);
            _activity = CreateActivityService(_profile);
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

        private ActivitySnapshot PassiveSnapshot(long steps)
        {
            return new ActivitySnapshot
            {
                providerId = DebugActivityProvider.ProviderIdValue,
                intervalStartUtc = _clock.UtcNow.AddMinutes(-5),
                intervalEndUtc = _clock.UtcNow,
                stepCount = steps,
                sourceType = ActivitySourceType.PhoneSensor,
                recordingType = ActivityRecordingType.Passive,
                quality = new ActivityQuality { hasStepEvidence = true },
            };
        }

        private void BeginLiveExpedition()
        {
            Assert.AreEqual(SessionStartError.None,
                _provider.StartSessionAsync(SessionType.Walk).GetAwaiter().GetResult());
            Assert.IsTrue(_activity.BeginExpedition(SessionType.Walk, _clock.UtcNow));
        }

        [Test]
        public void ProcessKillMidExpedition_RecoveryRestoresSuppressedPassiveStream()
        {
            BeginLiveExpedition();

            // The kill happens here: the persisted profile still carries the marker,
            // but no live provider session exists anymore (providers never survive
            // process death). A fresh service instance sees the same stale state a
            // restarted process would.
            var restarted = CreateActivityService(_profile);
            Assert.IsTrue(restarted.HasInterruptedSession);

            long suppressed = restarted.ProcessPassiveSnapshot(PassiveSnapshot(1200)).acceptedSteps;
            Assert.AreEqual(0, suppressed, "before recovery the stale marker still suppresses");
            Assert.AreEqual(0, _ledger.GetBalance());

            Assert.IsTrue(restarted.RecoverInterruptedSession());
            long credited = restarted.ProcessPassiveSnapshot(PassiveSnapshot(1200)).acceptedSteps;
            Assert.AreEqual(1200, credited);
            Assert.AreEqual(1200, _ledger.GetBalance());
            Assert.AreEqual(1200, _profile.lifetimeAcceptedSteps);
        }

        [Test]
        public void Recovery_CreditsNothingItself_AndIsIdempotent()
        {
            BeginLiveExpedition();

            long balanceBefore = _ledger.GetBalance();
            long stepsBefore = _profile.lifetimeAcceptedSteps;

            Assert.IsTrue(_activity.RecoverInterruptedSession());
            Assert.IsFalse(_activity.HasInterruptedSession);
            Assert.IsFalse(_activity.RecoverInterruptedSession(), "second recovery is a no-op");

            Assert.AreEqual(balanceBefore, _ledger.GetBalance());
            Assert.AreEqual(stepsBefore, _profile.lifetimeAcceptedSteps);
        }

        [Test]
        public void HealthyLifecycle_NeverTriggersRecovery()
        {
            BeginLiveExpedition();
            var result = MakeSessionResult("session.healthy", 500);
            _activity.ProcessSessionResult(result, growthEligible: false);

            Assert.IsFalse(_activity.HasInterruptedSession);
            Assert.IsFalse(_activity.RecoverInterruptedSession());
            Assert.AreEqual(500, _ledger.GetBalance());
        }

        [Test]
        public void LateDeliveredResult_AfterInterruption_StillCreditsExactlyOnce()
        {
            BeginLiveExpedition();
            _activity.RecoverInterruptedSession();

            // The provider result finally arrives after the restart.
            var delivered = MakeSessionResult("session.interrupted", 900);
            var first = _activity.ProcessSessionResult(delivered, growthEligible: false);
            Assert.AreEqual(900, first.acceptedSteps);

            // Crash-replay of the same delivery must never pay again.
            var replay = MakeSessionResult("session.interrupted", 900);
            var second = _activity.ProcessSessionResult(replay, growthEligible: false);
            Assert.AreEqual(0, second.acceptedSteps);
            Assert.AreEqual(900, _ledger.GetBalance());
            Assert.AreEqual(900, _profile.lifetimeAcceptedSteps);
        }

        [Test]
        public void DuplicateSessionId_DifferentPayloads_FirstDeliveryWins()
        {
            var first = _activity.ProcessSessionResult(MakeSessionResult("session.dup", 400), growthEligible: false);
            var second = _activity.ProcessSessionResult(MakeSessionResult("session.dup", 99999), growthEligible: false);

            Assert.AreEqual(400, first.acceptedSteps);
            Assert.AreEqual(0, second.acceptedSteps);
            Assert.AreEqual(400, _ledger.GetBalance(), "a replayed id must not mint the larger payload");
        }

        [Test]
        public void StopFailure_AbandonThenPassive_CreditsOnce()
        {
            BeginLiveExpedition();
            _activity.AbandonExpedition(); // stop failed -> controller abandons safely

            long first = _activity.ProcessPassiveSnapshot(PassiveSnapshot(700)).acceptedSteps;
            long second = _activity.ProcessPassiveSnapshot(PassiveSnapshot(700)).acceptedSteps;

            Assert.AreEqual(700, first);
            Assert.AreEqual(0, second, "the same physical window must not pay twice");
            Assert.AreEqual(700, _ledger.GetBalance());
        }

        [Test]
        public void PassiveWindowDuringActiveSession_DoesNotConsumeDedupOrCursor()
        {
            BeginLiveExpedition();

            // Suppressed reads must be invisible to dedup/cursors so the real window
            // can be paid later through the normal stream.
            Assert.AreEqual(0, _activity.ProcessPassiveSnapshot(PassiveSnapshot(300)).acceptedSteps);
            _activity.RecoverInterruptedSession();

            long credited = _activity.ProcessPassiveSnapshot(PassiveSnapshot(300)).acceptedSteps;
            Assert.AreEqual(300, credited);
            var cursor = _profile.activityState.lastSuccessfulSyncUtc.GetValueOrDefault();
            Assert.AreEqual(_clock.UtcNow, cursor, "cursor advanced with the credited interval");
        }

        [Test]
        public void InterruptedMarker_SurvivesSaveReload_AndRecoveryStaysStable()
        {
            string directory = Path.Combine(Path.GetTempPath(), "walkgame-recovery-tests", Guid.NewGuid().ToString("N"));
            try
            {
                var repository = new FileSaveRepository(
                    directory, "recovery.profile.json",
                    new JsonSaveSerializer(), new SaveMigrator(),
                    Log.Disabled, _clock);

                BeginLiveExpedition();
                Assert.AreEqual(SaveLoadResult.Success, repository.Save(_profile));

                // Reload through a brand-new object graph: this is the boot path.
                Assert.IsTrue(repository.TryLoad(out var loaded, out var loadResult));
                Assert.AreEqual(SaveLoadResult.Success, loadResult);
                Assert.IsNotNull(loaded.activityState.activeSession, "the interruption marker is durable");

                var restarted = CreateActivityService(loaded);
                Assert.IsTrue(restarted.HasInterruptedSession);
                Assert.IsTrue(restarted.RecoverInterruptedSession());

                // Recovery persists as the absence of the marker.
                Assert.AreEqual(SaveLoadResult.Success, repository.Save(loaded));
                Assert.IsTrue(repository.TryLoad(out var reloaded, out _));
                Assert.IsNull(reloaded.activityState.activeSession);
                Assert.AreEqual(loaded.vitalityBalance, reloaded.vitalityBalance);
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        private ActivitySessionResult MakeSessionResult(string sessionId, long steps)
        {
            return new ActivitySessionResult
            {
                sessionId = sessionId,
                type = SessionType.Walk,
                startUtc = _clock.UtcNow.AddMinutes(-20),
                endUtc = _clock.UtcNow,
                acceptedSteps = steps,
                verifiedDistanceMeters = 0,
                verifiedMovingSeconds = 1200,
                trustScore = 0.2f, // below bonus threshold: isolates base-step accounting
            };
        }
    }
}
