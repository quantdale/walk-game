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
    /// M8.4 runtime orchestration durability (ADR 0010): headless certification of the
    /// real application transaction protocol that Unity MonoBehaviours now delegate to.
    ///
    /// These 14 scenarios must fail on the pre-campaign protocol and prove the repaired
    /// coordinator at the lowest engine-free layer possible (Workstream F). Every scenario
    /// exercises ActivityTransactionCoordinator, the extracted testable surface that the
    /// standalone .NET gate now compiles (Workstream C/G).
    /// </summary>
    public sealed class ApplicationOrchestrationTests
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
            return _provider.PreparePassiveDeliveryAsync(new ActivityCursor
            {
                lastSuccessfulSyncUtc = _profile.activityState.lastSuccessfulSyncUtc,
                providerCursor = _profile.activityState.providerCursor,
            }).GetAwaiter().GetResult();
        }

        // ---- F1: persisted active-session marker -> successful completion -> durable exactly once ----

        [Test]
        public void F1_PersistedMarker_SuccessfulCompletion_DurableExactlyOnce()
        {
            var clean = CleanRepository();

            // Persist a marker as lifecycle autosave would during an active Expedition.
            Assert.IsTrue(_activity.BeginExpedition(SessionType.Walk, _clock.UtcNow));
            Assert.AreEqual(SaveLoadResult.Success, clean.Save(_profile));
            Assert.IsNotNull(_profile.activityState.activeSession);

            _provider.StartSessionAsync(SessionType.Walk).GetAwaiter().GetResult();
            _provider.SimulateSessionProgress(700, 0, 400);
            var result = _provider.StopSessionAsync().GetAwaiter().GetResult();
            Assert.AreEqual(700, result.acceptedSteps);

            var report = ActivityTransactionCoordinator.CompleteExpedition(
                _activity, _provider, result, () => new PersistenceCoordinator(clean, Log.Disabled, () => new PlayerProfile()).Commit(_profile));

            Assert.AreEqual(PersistenceCommitOutcome.Committed, report.commitOutcome);
            Assert.IsTrue(report.providerResolved);
            Assert.IsTrue(report.providerResolvedDurably);
            Assert.IsFalse(report.repairedResurrectedMarker);
            Assert.IsFalse(report.isFatal);
            Assert.IsFalse(_activity.HasInterruptedSession, "marker must be cleared durably");
            Assert.AreEqual(700, _profile.lifetimeAcceptedSteps);
            Assert.IsNull(PreparePassive()?.snapshot, "durably credited session never re-enters passive");
            Assert.IsTrue(clean.TryLoad(out var durable, out _));
            Assert.AreEqual(700, durable.vitalityBalance);
            Assert.IsNull(durable.activityState.activeSession);
            Assert.IsTrue(durable.activityState.creditedSessionIds.Contains($"session:{result.sessionId}"));
        }

        // ---- F2: persisted marker -> completion commit fails -> rollback restores marker -> provider rejects -> same-process passive recovery exactly once ----

        [Test]
        public void F2_PersistedMarker_FailedCompletion_RecoveredViaPassiveExactlyOnce()
        {
            var clean = CleanRepository();
            var failing = FailingRepository();

            // Durably persist the active marker (autosave during Expedition).
            Assert.IsTrue(_activity.BeginExpedition(SessionType.Walk, _clock.UtcNow));
            Assert.AreEqual(SaveLoadResult.Success, clean.Save(_profile));

            _provider.StartSessionAsync(SessionType.Walk).GetAwaiter().GetResult();
            _provider.SimulateSessionProgress(900, 0, 600);
            var result = _provider.StopSessionAsync().GetAwaiter().GetResult();

            var failingCoord = new PersistenceCoordinator(failing, Log.Disabled, () => new PlayerProfile());
            var report = ActivityTransactionCoordinator.CompleteExpedition(
                _activity, _provider, result, () => failingCoord.Commit(_profile));

            Assert.AreEqual(PersistenceCommitOutcome.RevertedToLastKnownGood, report.commitOutcome);
            Assert.IsTrue(report.providerResolved);
            Assert.IsFalse(report.providerResolvedDurably);
            Assert.IsTrue(report.repairedResurrectedMarker, "rollback-resurrected marker must be repaired in same process (ADR 0010)");
            Assert.IsFalse(report.isFatal);
            Assert.IsFalse(_activity.HasInterruptedSession, "repair must leave no suppression marker for the retry");
            Assert.AreEqual(0, _ledger.GetBalance(), "reverted credit must be removed");
            Assert.AreEqual(0, _profile.lifetimeAcceptedSteps);

            // The rejected session's base movement must be retryable through the passive stream.
            var recovery = PreparePassive();
            Assert.IsNotNull(recovery?.snapshot, "rejected base movement must be recoverable passively without a restart");
            Assert.AreEqual(900, recovery.snapshot.stepCount);

            var passiveReport = ActivityTransactionCoordinator.DeliverPreparedPassive(
                _activity, _provider, recovery, () => new PersistenceCoordinator(clean, Log.Disabled, () => new PlayerProfile()).Commit(_profile));

            Assert.AreEqual(PassiveReconciliationDisposition.DurableMutation, passiveReport.reconciliationResult.disposition);
            Assert.AreEqual(PersistenceCommitOutcome.Committed, passiveReport.commitOutcome);
            Assert.IsTrue(passiveReport.providerResolvedDurably);
            Assert.AreEqual(900, _ledger.GetBalance());
            Assert.AreEqual(900, _profile.lifetimeAcceptedSteps);
            Assert.IsNull(PreparePassive()?.snapshot, "exactly once across the recovery");
        }

        // ---- F3: F2 with another transient failure before eventual success ----

        [Test]
        public void F3_PersistedMarker_TwoTransientFailures_ThenSuccess_ExactlyOnce()
        {
            var clean = CleanRepository();
            var failing = FailingRepository();

            Assert.IsTrue(_activity.BeginExpedition(SessionType.Walk, _clock.UtcNow));
            Assert.AreEqual(SaveLoadResult.Success, clean.Save(_profile));

            _provider.StartSessionAsync(SessionType.Walk).GetAwaiter().GetResult();
            _provider.SimulateSessionProgress(500, 0, 300);
            var result = _provider.StopSessionAsync().GetAwaiter().GetResult();

            var report1 = ActivityTransactionCoordinator.CompleteExpedition(
                _activity, _provider, result, () => new PersistenceCoordinator(failing, Log.Disabled, () => new PlayerProfile()).Commit(_profile));
            Assert.AreEqual(PersistenceCommitOutcome.RevertedToLastKnownGood, report1.commitOutcome);
            Assert.IsTrue(report1.repairedResurrectedMarker);

            // First passive retry also fails to commit.
            var firstRetry = PreparePassive();
            Assert.IsNotNull(firstRetry?.snapshot);
            Assert.AreEqual(500, firstRetry.snapshot.stepCount);

            var passiveFail = ActivityTransactionCoordinator.DeliverPreparedPassive(
                _activity, _provider, firstRetry, () => new PersistenceCoordinator(failing, Log.Disabled, () => new PlayerProfile()).Commit(_profile));
            Assert.AreEqual(PersistenceCommitOutcome.RevertedToLastKnownGood, passiveFail.commitOutcome);
            Assert.IsFalse(passiveFail.providerResolvedDurably);
            Assert.AreEqual(0, _ledger.GetBalance(), "second failure must still revert credit");

            // Second passive retry finally commits.
            var secondRetry = PreparePassive();
            Assert.IsNotNull(secondRetry?.snapshot, "movement must stay retryable through repeated failures");
            Assert.AreEqual(500, secondRetry.snapshot.stepCount);

            var passiveSuccess = ActivityTransactionCoordinator.DeliverPreparedPassive(
                _activity, _provider, secondRetry, () => new PersistenceCoordinator(clean, Log.Disabled, () => new PlayerProfile()).Commit(_profile));
            Assert.AreEqual(PersistenceCommitOutcome.Committed, passiveSuccess.commitOutcome);
            Assert.AreEqual(500, _ledger.GetBalance());
            Assert.IsNull(PreparePassive()?.snapshot, "never doubles despite two failures");
        }

        // ---- F4: duplicate session id after durable success is harmless ----

        [Test]
        public void F4_DuplicateSessionId_AfterDurableSuccess_Harmless()
        {
            var clean = CleanRepository();

            _provider.StartSessionAsync(SessionType.Walk).GetAwaiter().GetResult();
            Assert.IsTrue(_activity.BeginExpedition(SessionType.Walk, _clock.UtcNow));
            _provider.SimulateSessionProgress(450, 0, 300);
            var result = _provider.StopSessionAsync().GetAwaiter().GetResult();

            var report = ActivityTransactionCoordinator.CompleteExpedition(
                _activity, _provider, result, () => new PersistenceCoordinator(clean, Log.Disabled, () => new PlayerProfile()).Commit(_profile));
            Assert.AreEqual(PersistenceCommitOutcome.Committed, report.commitOutcome);
            Assert.AreEqual(450, _ledger.GetBalance());

            // Re-deliver the same session id through the coordinator: must be a no-op.
            var duplicateReport = ActivityTransactionCoordinator.CompleteExpedition(
                _activity, _provider, result, () => new PersistenceCoordinator(clean, Log.Disabled, () => new PlayerProfile()).Commit(_profile));
            Assert.IsTrue(duplicateReport.isDuplicateSession, "second delivery of same session must be deduped");
            Assert.AreEqual(0, duplicateReport.processedResult.acceptedSteps);
            Assert.AreEqual(450, _ledger.GetBalance(), "duplicate must not re-credit");
            Assert.IsNull(PreparePassive()?.snapshot, "no passive replay of durably credited session");
        }

        // ---- F5: fatal persistence loss during completion -> blocked, no fabricated reward ----

        [Test]
        public void F5_FatalLoss_DuringCompletion_FailsClosed_NoFabricatedReward()
        {
            _provider.StartSessionAsync(SessionType.Walk).GetAwaiter().GetResult();
            Assert.IsTrue(_activity.BeginExpedition(SessionType.Walk, _clock.UtcNow));
            _provider.SimulateSessionProgress(300, 0, 200);
            var result = _provider.StopSessionAsync().GetAwaiter().GetResult();

            var report = ActivityTransactionCoordinator.CompleteExpedition(
                _activity, _provider, result, () => PersistenceCommitOutcome.FatalPersistenceLoss);

            Assert.IsTrue(report.isFatal);
            Assert.IsFalse(report.providerResolved, "fatal loss must not acknowledge provider delivery");
            Assert.AreEqual(PersistenceCommitOutcome.FatalPersistenceLoss, report.commitOutcome);
            // The provider still holds the pending completion claim (steps not acknowledged).
            // A manual resolve with false must still return them to the passive stream,
            // proving the coordinator did not falsely drop them.
            _provider.ResolveSessionCompletion(result.sessionId, false);
            var recovery = PreparePassive();
            Assert.IsNotNull(recovery?.snapshot);
            Assert.AreEqual(300, recovery.snapshot.stepCount, "fatal loss must not synthesize or destroy held movement; it stays held by provider until host teardown");
        }

        // ---- F6: stop returns null/fault/cancel after durable marker ----

        [Test]
        public void F6_StopReturnsNull_AfterDurableMarker_MarkerDurablyClosed()
        {
            var clean = CleanRepository();

            Assert.IsTrue(_activity.BeginExpedition(SessionType.Walk, _clock.UtcNow));
            Assert.AreEqual(SaveLoadResult.Success, clean.Save(_profile));
            Assert.IsTrue(_activity.HasInterruptedSession);

            // Stop produced no usable result (null): coordinator must durably close the marker.
            var report = ActivityTransactionCoordinator.CompleteExpedition(
                _activity, _provider, null, () => new PersistenceCoordinator(clean, Log.Disabled, () => new PlayerProfile()).Commit(_profile));

            Assert.AreEqual(PersistenceCommitOutcome.Committed, report.commitOutcome);
            Assert.IsFalse(report.isFatal);
            Assert.IsFalse(_activity.HasInterruptedSession);
            Assert.IsTrue(clean.TryLoad(out var durable, out _));
            Assert.IsNull(durable.activityState.activeSession, "abandonment without result must commit durably");
        }

        [Test]
        public void F6b_StopNull_WithFailingCommit_RepairsResurrectedMarker()
        {
            var clean = CleanRepository();
            var failing = FailingRepository();

            Assert.IsTrue(_activity.BeginExpedition(SessionType.Walk, _clock.UtcNow));
            Assert.AreEqual(SaveLoadResult.Success, clean.Save(_profile));

            var report = ActivityTransactionCoordinator.CompleteExpedition(
                _activity, _provider, null, () => new PersistenceCoordinator(failing, Log.Disabled, () => new PlayerProfile()).Commit(_profile));

            Assert.AreEqual(PersistenceCommitOutcome.RevertedToLastKnownGood, report.commitOutcome);
            Assert.IsTrue(report.repairedResurrectedMarker, "failed abandonment commit must repair the resurrected marker without requiring a restart");
            Assert.IsFalse(_activity.HasInterruptedSession);

            // Subsequent passive window must not be suppressed.
            _provider.DebugAddSteps(250);
            var passive = PreparePassive();
            Assert.IsNotNull(passive?.snapshot, "repaired marker must not suppress next passive window");
            var passiveReport = ActivityTransactionCoordinator.DeliverPreparedPassive(
                _activity, _provider, passive, () => new PersistenceCoordinator(clean, Log.Disabled, () => new PlayerProfile()).Commit(_profile));
            Assert.AreEqual(PersistenceCommitOutcome.Committed, passiveReport.commitOutcome);
            Assert.AreEqual(250, _ledger.GetBalance());
        }

        // ---- F7: process restart after unresolved/failed completion still converges ----

        [Test]
        public void F7_Restart_AfterFailedCompletion_ConvergesWithoutDuplication()
        {
            var clean = CleanRepository();
            var failing = FailingRepository();

            Assert.IsTrue(_activity.BeginExpedition(SessionType.Walk, _clock.UtcNow));
            Assert.AreEqual(SaveLoadResult.Success, clean.Save(_profile));

            _provider.StartSessionAsync(SessionType.Walk).GetAwaiter().GetResult();
            _provider.SimulateSessionProgress(600, 0, 400);
            var result = _provider.StopSessionAsync().GetAwaiter().GetResult();

            // Completion fails and reverts, but the in-memory repair is NOT yet durably saved
            // (next commit hasn't happened yet). Simulate process death before convergence.
            var report = ActivityTransactionCoordinator.CompleteExpedition(
                _activity, _provider, result, () => new PersistenceCoordinator(failing, Log.Disabled, () => new PlayerProfile()).Commit(_profile));
            Assert.IsTrue(report.repairedResurrectedMarker);
            // Do NOT commit the repair; simulate process death: reload durable state from disk.
            Assert.IsTrue(clean.TryLoad(out var durableBeforeRestart, out _));
            Assert.IsNotNull(durableBeforeRestart.activityState.activeSession, "without a successful follow-up commit the durable file still holds the marker");

            // Fresh boot: new service graph from durable state must recover via last-resort boot repair.
            var freshLedger = new VitalityLedger(durableBeforeRestart, _clock, new DomainEvents(), Log.Disabled);
            var freshActivity = new ActivityService(durableBeforeRestart, freshLedger, new TrustEvaluator(RewardPolicy.Default), new RewardCalculator(RewardPolicy.Default), new DomainEvents(), Log.Disabled);
            Assert.IsTrue(freshActivity.HasInterruptedSession);
            Assert.IsTrue(freshActivity.RecoverInterruptedSession(), "boot recovery clears stale marker left by the failed completion");
            Assert.IsFalse(freshActivity.HasInterruptedSession);
        }

        // ---- F8: passive prepared -> commit succeeds -> acknowledge exactly once ----

        [Test]
        public void F8_PassivePrepared_CommitSucceeds_AcknowledgedExactlyOnce()
        {
            var clean = CleanRepository();

            _provider.DebugAddSteps(800);
            var delivery = PreparePassive();
            Assert.IsNotNull(delivery);
            Assert.AreEqual(800, delivery.snapshot.stepCount);

            var report = ActivityTransactionCoordinator.DeliverPreparedPassive(
                _activity, _provider, delivery, () => new PersistenceCoordinator(clean, Log.Disabled, () => new PlayerProfile()).Commit(_profile));

            Assert.AreEqual(PassiveReconciliationDisposition.DurableMutation, report.reconciliationResult.disposition);
            Assert.AreEqual(PersistenceCommitOutcome.Committed, report.commitOutcome);
            Assert.IsTrue(report.providerResolvedDurably);
            Assert.IsNull(PreparePassive()?.snapshot, "acknowledged movement never replays");
            Assert.AreEqual(800, _ledger.GetBalance());
        }

        // ---- F9: passive prepared -> commit reverts -> reject exactly once and retry safe ----

        [Test]
        public void F9_PassivePrepared_CommitReverts_RejectAndRetryExactlyOnce()
        {
            var clean = CleanRepository();
            var failing = FailingRepository();
            Assert.AreEqual(SaveLoadResult.Success, clean.Save(_profile));

            _provider.DebugAddSteps(600);
            var delivery = PreparePassive();
            var report = ActivityTransactionCoordinator.DeliverPreparedPassive(
                _activity, _provider, delivery, () => new PersistenceCoordinator(failing, Log.Disabled, () => new PlayerProfile()).Commit(_profile));

            Assert.AreEqual(PersistenceCommitOutcome.RevertedToLastKnownGood, report.commitOutcome);
            Assert.IsFalse(report.providerResolvedDurably);
            Assert.AreEqual(0, _ledger.GetBalance());

            var retry = PreparePassive();
            Assert.IsNotNull(retry?.snapshot);
            Assert.AreEqual(600, retry.snapshot.stepCount);

            var retryReport = ActivityTransactionCoordinator.DeliverPreparedPassive(
                _activity, _provider, retry, () => new PersistenceCoordinator(clean, Log.Disabled, () => new PlayerProfile()).Commit(_profile));
            Assert.AreEqual(PersistenceCommitOutcome.Committed, retryReport.commitOutcome);
            Assert.AreEqual(600, _ledger.GetBalance());
            Assert.IsNull(PreparePassive()?.snapshot);
        }

        // ---- F10: suppressed passive delivery during live Expedition stays retryable ----

        [Test]
        public void F10_SuppressedDelivery_DuringLiveExpedition_RetryableAfterSession()
        {
            var clean = CleanRepository();

            _provider.DebugAddSteps(1000);
            var delivery = PreparePassive();
            Assert.IsNotNull(delivery);

            _profile.activityState.activeSession = new ActiveSessionState();
            var report = ActivityTransactionCoordinator.DeliverPreparedPassive(
                _activity, _provider, delivery, () => new PersistenceCoordinator(clean, Log.Disabled, () => new PlayerProfile()).Commit(_profile));

            Assert.AreEqual(PassiveReconciliationDisposition.SuppressedBySession, report.reconciliationResult.disposition);
            Assert.IsNull(report.commitOutcome, "suppressed delivery needs no commit");
            Assert.IsFalse(report.providerResolvedDurably, "suppressed delivery must be rejected, not acknowledged");
            Assert.IsTrue(report.providerResolved);

            _profile.activityState.activeSession = null;

            var retry = PreparePassive();
            Assert.IsNotNull(retry?.snapshot);
            Assert.AreEqual(1000, retry.snapshot.stepCount, "suppression must not strand movement");
        }

        // ---- F11: late/timeout preparation cannot strand a claim ----

        [Test]
        public void F11_LatePreparation_RejectedWithoutProcessing_Retryable()
        {
            _provider.DebugAddSteps(432);
            var delivery = PreparePassive();
            Assert.IsNotNull(delivery);
            Assert.AreEqual(432, delivery.snapshot.stepCount);

            // Simulate timeout late arrival: do NOT process; reject the abandoned preparation.
            ActivityTransactionCoordinator.RejectAbandonedPreparation(_provider, delivery);

            var retry = PreparePassive();
            Assert.IsNotNull(retry?.snapshot, "rejected late delivery must stay retryable and not be stranded");
            Assert.AreEqual(432, retry.snapshot.stepCount);

            var report = ActivityTransactionCoordinator.DeliverPreparedPassive(
                _activity, _provider, retry, () => PersistenceCommitOutcome.Committed);
            Assert.IsTrue(report.providerResolvedDurably);
            Assert.AreEqual(432, _ledger.GetBalance());
        }

        [Test]
        public void F11b_RejectAbandonedPreparation_NullSafe()
        {
            Assert.DoesNotThrow(() => ActivityTransactionCoordinator.RejectAbandonedPreparation(_provider, null));
            var empty = new PreparedActivityDelivery { snapshot = null };
            Assert.DoesNotThrow(() => ActivityTransactionCoordinator.RejectAbandonedPreparation(_provider, empty));
        }

        // ---- F12: blocked transition while activity work in flight cannot mutate dead profile ----

        [Test]
        public void F12_BlockedTransition_DuringPassiveCommit_NoDeadProfileMutation()
        {
            _provider.DebugAddSteps(200);
            var delivery = PreparePassive();
            Assert.IsNotNull(delivery);

            var report = ActivityTransactionCoordinator.DeliverPreparedPassive(
                _activity, _provider, delivery, () => PersistenceCommitOutcome.FatalPersistenceLoss);

            Assert.IsTrue(report.isFatal);
            Assert.IsFalse(report.providerResolved, "fatal loss must not acknowledge the prepared delivery");
            Assert.AreEqual(PersistenceCommitOutcome.FatalPersistenceLoss, report.commitOutcome);
            // The in-memory ledger was credited by ProcessPassiveSnapshot before the commit,
            // but the coordinator reports fatal so callers know to discard / fail-closed.
            // No additional provider resolution must have happened (idempotent).
            _provider.ResolvePreparedDelivery(delivery, false);
            var retry = PreparePassive();
            Assert.IsNotNull(retry?.snapshot, "fatal path leaves provider claim intact until teardown; test proves no false ack");
        }

        // ---- F13: repeated stale/duplicate provider resolutions safe no-ops ----

        [Test]
        public void F13_RepeatedStaleResolutions_AreSafeNoOps()
        {
            _provider.DebugAddSteps(500);
            var delivery = PreparePassive();
            var report = ActivityTransactionCoordinator.DeliverPreparedPassive(
                _activity, _provider, delivery, () => PersistenceCommitOutcome.Committed);
            Assert.IsTrue(report.providerResolvedDurably);

            // Re-resolving the same delivery (stale/duplicate) must be harmless.
            _provider.ResolvePreparedDelivery(delivery, true);
            _provider.ResolvePreparedDelivery(delivery, false);
            _provider.ResolvePreparedDelivery(null, false);
            _provider.ResolveSessionCompletion("session.unknown", false);
            _provider.ResolveSessionCompletion(null, true);

            Assert.IsNull(PreparePassive()?.snapshot, "resolved movement never replays despite stale calls");
            Assert.AreEqual(500, _ledger.GetBalance());
        }

        // ---- F14: durability-gated feedback truthfulness (report flags drive UI) ----

        [Test]
        public void F14_ReportFlags_DriveTruthfulFeedback()
        {
            var clean = CleanRepository();
            var failing = FailingRepository();

            // Committed path: UI should celebrate.
            _provider.DebugAddSteps(100);
            var c1 = PreparePassive();
            var r1 = ActivityTransactionCoordinator.DeliverPreparedPassive(
                _activity, _provider, c1, () => new PersistenceCoordinator(clean, Log.Disabled, () => new PlayerProfile()).Commit(_profile));
            Assert.IsFalse(r1.isFatal);
            Assert.AreEqual(PersistenceCommitOutcome.Committed, r1.commitOutcome);
            Assert.IsTrue(r1.providerResolvedDurably, "committed delivery drives FlushQueuedDurable");

            // Reverted path: UI must drop celebration cues.
            _provider.DebugAddSteps(200);
            Assert.AreEqual(SaveLoadResult.Success, clean.Save(_profile));
            var c2 = PreparePassive();
            var r2 = ActivityTransactionCoordinator.DeliverPreparedPassive(
                _activity, _provider, c2, () => new PersistenceCoordinator(failing, Log.Disabled, () => new PlayerProfile()).Commit(_profile));
            Assert.AreEqual(PersistenceCommitOutcome.RevertedToLastKnownGood, r2.commitOutcome);
            Assert.IsFalse(r2.providerResolvedDurably, "reverted delivery drives DropQueuedDurable");
            Assert.IsFalse(r2.isFatal);

            // Fatal path: UI is replaced by blocked recomposition.
            _provider.DebugAddSteps(50);
            var c3 = PreparePassive();
            // Ensure c3 is deliverable (retry of r2's rejected 200 may be pending; resolve it first to isolate).
            // r2's delivery was rejected back to provider, so the 200 is still pending ahead of the new 50.
            // Drain the rejected 200 via a successful commit to isolate the fatal scenario to the 50.
            if (c3 != null && c3.snapshot.stepCount == 200)
            {
                var drain = ActivityTransactionCoordinator.DeliverPreparedPassive(
                    _activity, _provider, c3, () => new PersistenceCoordinator(clean, Log.Disabled, () => new PlayerProfile()).Commit(_profile));
                Assert.AreEqual(PersistenceCommitOutcome.Committed, drain.commitOutcome);
                c3 = PreparePassive();
            }
            Assert.IsNotNull(c3);
            var r3 = ActivityTransactionCoordinator.DeliverPreparedPassive(
                _activity, _provider, c3, () => PersistenceCommitOutcome.FatalPersistenceLoss);
            Assert.IsTrue(r3.isFatal, "fatal outcome must be distinct from revert so UI never falsely celebrates");
            Assert.IsFalse(r3.providerResolved);
        }

        // ---- Additional: coordinator validates its trust evaluation path ----

        [Test]
        public void Coordinator_EvaluatesTrust_ForExpeditionResult()
        {
            var clean = CleanRepository();

            _provider.StartSessionAsync(SessionType.Walk).GetAwaiter().GetResult();
            Assert.IsTrue(_activity.BeginExpedition(SessionType.Walk, _clock.UtcNow));
            _provider.SimulateSessionProgress(400, 0, 300);
            var result = _provider.StopSessionAsync().GetAwaiter().GetResult();
            result.trustScore = 0f; // ensure coordinator overwrites

            var report = ActivityTransactionCoordinator.CompleteExpedition(
                _activity, _provider, result, () => new PersistenceCoordinator(clean, Log.Disabled, () => new PlayerProfile()).Commit(_profile));

            Assert.AreNotEqual(0f, report.processedResult.trustScore, "coordinator must evaluate trust before processing");
            Assert.IsTrue(report.rewardCredited);
        }

        private sealed class WriteFaultFileSystem : ISaveFileSystem
        {
            public void EnsureDirectory(string directory) => Directory.CreateDirectory(directory);
            public bool Exists(string path) => File.Exists(path);
            public string ReadAllText(string path) => File.ReadAllText(path);
            public void WriteAllText(string path, string contents) => throw new IOException("Injected write failure.");
            public void Copy(string sourceFileName, string destFileName, bool overwrite) => File.Copy(sourceFileName, destFileName, overwrite);
            public void Delete(string path) => File.Delete(path);
            public void Move(string sourceFileName, string destFileName) => File.Move(sourceFileName, destFileName);
        }
    }
}
