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
    /// M8.5 runtime-ownership orchestration scenarios (OpenSpec functional matrix):
    /// the debug/vehicle active-session paths through the shared transaction protocol
    /// on failed persistence, hung-stop convergence, and durability-gated presentation.
    /// Mirrors the exact ordering the Unity UiComposer.VehicleSessionRoutine and
    /// ExpeditionController now delegate to, so a green headless run certifies them.
    /// </summary>
    public sealed class RuntimeOwnershipOrchestrationTests
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
            _clock = new MutableClock(new DateTime(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc));
            _profile = new PlayerProfile();
            _events = new DomainEvents();
            _ledger = new VitalityLedger(_profile, _clock, _events, Log.Disabled);
            _provider = new DebugActivityProvider(_clock);
            _provider.DebugSimulateReboot();
            _activity = new ActivityService(
                _profile,
                _ledger,
                new TrustEvaluator(RewardPolicy.Default),
                new RewardCalculator(RewardPolicy.Default),
                _events,
                Log.Disabled);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        private PersistenceCoordinator Coordinator(FileSaveRepository repository)
        {
            return new PersistenceCoordinator(repository, Log.Disabled, () => new PlayerProfile());
        }

        private FileSaveRepository CleanRepository()
        {
            return new FileSaveRepository(_directory, "profile.json", new JsonSaveSerializer(), new SaveMigrator(), Log.Disabled, _clock);
        }

        private FileSaveRepository FailingRepository()
        {
            return new FileSaveRepository(_directory, "profile.json", new JsonSaveSerializer(), new SaveMigrator(), Log.Disabled, _clock, new WriteFaultFileSystem());
        }

        private PreparedActivityDelivery PreparePassive()
        {
            return _provider.PreparePassiveDeliveryAsync(new ActivityCursor()).GetAwaiter().GetResult();
        }

        // ---- F10: vehicle/debug completion on failed persistence repairs marker -----

        [Test]
        public void VehicleStyleCompletion_PersistedMarker_FailedCommit_RepairsMarkerAndRecovers()
        {
            var clean = CleanRepository();
            var failing = FailingRepository();

            // A lifecycle autosave persisted the marker while the session ran.
            Assert.IsTrue(_activity.BeginExpedition(SessionType.Walk, _clock.UtcNow));
            Assert.AreEqual(SaveLoadResult.Success, clean.Save(_profile));

            _provider.StartSessionAsync(SessionType.Walk).GetAwaiter().GetResult();
            _provider.SimulateVehicleDrive(minutes: 30); // vehicle-like: huge distance, few steps
            var result = _provider.StopSessionAsync().GetAwaiter().GetResult();

            // The exact coordinator call shape used by the debug vehicle fixture
            // (location-evidence trust facts ride along; sequence stays the coordinator's).
            var report = ActivityTransactionCoordinator.CompleteExpedition(
                _activity,
                _provider,
                result,
                () => Coordinator(failing).Commit(_profile),
                growthEligible: false,
                hasLocationEvidence: true);

            Assert.AreEqual(PersistenceCommitOutcome.RevertedToLastKnownGood, report.commitOutcome);
            Assert.IsTrue(report.repairedResurrectedMarker,
                "the vehicle path must perform the same rollback-marker repair as normal Expeditions");
            Assert.IsFalse(_activity.HasInterruptedSession, "no suppression marker may survive the repair");

            // Rejected base movement is retryable in the same process exactly once...
            var recovery = PreparePassive();
            Assert.IsNotNull(recovery?.snapshot, "vehicle base movement must stay retryable without a restart");
            Assert.AreEqual(result.acceptedSteps, recovery.snapshot.stepCount);

            var passiveReport = ActivityTransactionCoordinator.DeliverPreparedPassive(
                _activity, _provider, recovery, () => Coordinator(clean).Commit(_profile));
            Assert.AreEqual(PersistenceCommitOutcome.Committed, passiveReport.commitOutcome);
            Assert.IsTrue(passiveReport.providerResolvedDurably);
            Assert.AreEqual(recovery.snapshot.stepCount, _profile.lifetimeAcceptedSteps,
                "only the retryable base movement was credited, exactly once");
        }

        [Test]
        public void VehicleStyleCompletion_BonusRejected_BaseStepsKept()
        {
            _provider.StartSessionAsync(SessionType.Walk).GetAwaiter().GetResult();
            Assert.IsTrue(_activity.BeginExpedition(SessionType.Walk, _clock.UtcNow));
            _provider.SimulateVehicleDrive(minutes: 30);
            var result = _provider.StopSessionAsync().GetAwaiter().GetResult();

            var report = ActivityTransactionCoordinator.CompleteExpedition(
                _activity,
                _provider,
                result,
                () => Coordinator(CleanRepository()).Commit(_profile),
                growthEligible: false,
                hasLocationEvidence: true);

            Assert.AreEqual(PersistenceCommitOutcome.Committed, report.commitOutcome);
            Assert.AreEqual(0, result.bonusBreakdown.totalBonus,
                "vehicle-like movement earns no performance bonus");
            Assert.AreEqual(result.acceptedSteps, _profile.lifetimeAcceptedSteps,
                "base steps still count");
        }

        [Test]
        public void NoResultDebugStop_FailedCommit_UsesSharedDurableCloseAndRepair()
        {
            var clean = CleanRepository();
            var failing = FailingRepository();

            Assert.IsTrue(_activity.BeginExpedition(SessionType.Walk, _clock.UtcNow));
            Assert.AreEqual(SaveLoadResult.Success, clean.Save(_profile));

            _provider.StartSessionAsync(SessionType.Walk).GetAwaiter().GetResult();
            _provider.SimulateSessionProgress(200, 0, 60);
            var result = _provider.StopSessionAsync().GetAwaiter().GetResult();

            // The stop OBSERVATION produced no usable result (fault/null): the shared
            // no-result path durably closes the marker; the commit fails and reverts.
            var emptyReport = ActivityTransactionCoordinator.CompleteExpedition(
                _activity, _provider, null, () => Coordinator(failing).Commit(_profile));
            Assert.AreEqual(PersistenceCommitOutcome.RevertedToLastKnownGood, emptyReport.commitOutcome);
            Assert.IsTrue(emptyReport.repairedResurrectedMarker,
                "the no-result path performs the same resurrection repair");
            Assert.IsNull(emptyReport.processedResult);
            Assert.IsFalse(_activity.HasInterruptedSession);

            // The provider-held base movement converges through its cleanup owner:
            // a late real result resolves NON-durably back to the passive stream.
            ProviderOperations.AbandonSessionStop(
                CompletedTaskWith(result), new OperationLease(), _provider);

            var recovery = PreparePassive();
            Assert.IsNotNull(recovery?.snapshot);
            Assert.AreEqual(200, recovery.snapshot.stepCount);
        }

        [Test]
        public void HungStop_AbandonedThenLateResult_ConvergesWithoutDoubleCredit()
        {
            var clean = CleanRepository();

            Assert.IsTrue(_activity.BeginExpedition(SessionType.Walk, _clock.UtcNow));
            Assert.AreEqual(SaveLoadResult.Success, clean.Save(_profile));

            _provider.StartSessionAsync(SessionType.Walk).GetAwaiter().GetResult();
            _provider.SimulateSessionProgress(350, 0, 120);

            // The controller hits its stop policy bound while the provider stop is still
            // outstanding: transfer ownership, then durably close the canonical marker.
            var hungStop = new TaskCompletionSource<ActivitySessionResult>();
            var lease = new OperationLease();
            ProviderOperations.AbandonSessionStop(hungStop.Task, lease, _provider);

            var closeReport = ActivityTransactionCoordinator.CompleteExpedition(
                _activity, _provider, null, () => Coordinator(clean).Commit(_profile));
            Assert.AreEqual(PersistenceCommitOutcome.Committed, closeReport.commitOutcome,
                "the abandonment close itself must be durable");
            Assert.IsFalse(_activity.HasInterruptedSession);

            // The stop task eventually completes with the REAL result: the cleanup owner
            // resolves it non-durably (no reward was processed for it).
            var lateResult = _provider.StopSessionAsync().GetAwaiter().GetResult();
            Assert.AreEqual(350, lateResult.acceptedSteps);
            hungStop.SetResult(lateResult);

            // Base movement is now passively creditable exactly once.
            var recovery = PreparePassive();
            Assert.IsNotNull(recovery?.snapshot);
            Assert.AreEqual(350, recovery.snapshot.stepCount);

            var delivery = ActivityTransactionCoordinator.DeliverPreparedPassive(
                _activity, _provider, recovery, () => Coordinator(clean).Commit(_profile));
            Assert.AreEqual(PersistenceCommitOutcome.Committed, delivery.commitOutcome);
            Assert.AreEqual(350, _profile.lifetimeAcceptedSteps, "movement credited exactly once overall");
        }

        // ---- F12/F13: durability-gated player truth --------------------------------

        [Test]
        public void Presentation_CommittedCompletion_ShowsPositiveRewardCopy()
        {
            _provider.StartSessionAsync(SessionType.Walk).GetAwaiter().GetResult();
            Assert.IsTrue(_activity.BeginExpedition(SessionType.Walk, _clock.UtcNow));
            _provider.SimulateSessionProgress(800, 0, 400);
            var result = _provider.StopSessionAsync().GetAwaiter().GetResult();

            var report = ActivityTransactionCoordinator.CompleteExpedition(
                _activity, _provider, result, () => Coordinator(CleanRepository()).Commit(_profile));

            Assert.AreEqual(PersistenceCommitOutcome.Committed, report.commitOutcome);
            StringAssert.Contains("+800 steps", ExpeditionResultPresentation.RewardSummary(report));
            Assert.AreEqual("Expedition complete", ExpeditionResultPresentation.CompletionStatus(report));
        }

        [Test]
        public void Presentation_RevertedCompletion_ShowsNoPositiveReward_AndTruthfulRetryCopy()
        {
            var failing = FailingRepository();

            _provider.StartSessionAsync(SessionType.Walk).GetAwaiter().GetResult();
            Assert.IsTrue(_activity.BeginExpedition(SessionType.Walk, _clock.UtcNow));
            _provider.SimulateSessionProgress(800, 0, 400);
            var result = _provider.StopSessionAsync().GetAwaiter().GetResult();

            var report = ActivityTransactionCoordinator.CompleteExpedition(
                _activity, _provider, result, () => Coordinator(failing).Commit(_profile));

            Assert.AreEqual(PersistenceCommitOutcome.RevertedToLastKnownGood, report.commitOutcome);
            Assert.IsEmpty(ExpeditionResultPresentation.RewardSummary(report),
                "a rolled-back reward must NEVER be displayed as earned");
            StringAssert.Contains("could not be saved",
                ExpeditionResultPresentation.CompletionStatus(report));
            StringAssert.Contains("stay safe",
                ExpeditionResultPresentation.CompletionStatus(report));
        }

        [Test]
        public void Presentation_FatalLoss_ShowsRecoveryCopyOnly_NoRewardSummary()
        {
            // Both slots unreadable + writes failing: fatal persistence loss.
            var clean = CleanRepository();
            Assert.AreEqual(SaveLoadResult.Success, clean.Save(_profile));

            _provider.StartSessionAsync(SessionType.Walk).GetAwaiter().GetResult();
            Assert.IsTrue(_activity.BeginExpedition(SessionType.Walk, _clock.UtcNow));
            _provider.SimulateSessionProgress(500, 0, 300);
            var result = _provider.StopSessionAsync().GetAwaiter().GetResult();

            File.WriteAllText(Path.Combine(_directory, "profile.json"), "{ rot");
            File.WriteAllText(Path.Combine(_directory, "profile.json.bak"), "{ rot too");

            var report = ActivityTransactionCoordinator.CompleteExpedition(
                _activity, _provider, result,
                () => new PersistenceCoordinator(
                    new FileSaveRepository(_directory, "profile.json", new JsonSaveSerializer(), new SaveMigrator(), Log.Disabled, _clock, new WriteFaultFileSystem()),
                    Log.Disabled, () => new PlayerProfile()).Commit(_profile));

            Assert.IsTrue(report.isFatal);
            Assert.IsEmpty(ExpeditionResultPresentation.RewardSummary(report),
                "uncommitted reward must not be presented as earned");
            StringAssert.Contains("recovery", ExpeditionResultPresentation.CompletionStatus(report));
        }

        private static Task<T> CompletedTaskWith<T>(T value)
        {
            var source = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            source.SetResult(value);
            return source.Task;
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
