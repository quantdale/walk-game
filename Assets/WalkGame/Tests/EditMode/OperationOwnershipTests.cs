using System;
using System.Threading.Tasks;
using NUnit.Framework;
using WalkGame.Activity;
using WalkGame.Core;

namespace WalkGame.Tests
{
    /// <summary>
    /// M8.5 operation ownership (runtime-ownership, invariants I2-I4): every abandoned
    /// provider operation reaches exactly one terminal owner. A completion that wins the
    /// race is processed once; an abandonment that wins installs a deterministic cleanup
    /// owner so any late result converges provider state without stranding claims,
    /// without advancing cursors, and without fabricating durable acknowledgment.
    /// These are the deterministic headless proofs of the timeout/completion race the
    /// Unity ticker previously closed with a hard 30s drain ceiling.
    /// </summary>
    public sealed class OperationOwnershipTests
    {
        private MutableClock _clock;
        private DebugActivityProvider _provider;

        [SetUp]
        public void SetUp()
        {
            _clock = new MutableClock(new DateTime(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc));
            _provider = new DebugActivityProvider(_clock);
            _provider.DebugSimulateReboot();
        }

        private static TaskCompletionSource<T> Pending<T>()
        {
            // No RunContinuationsAsynchronously: continuations registered with
            // ExecuteSynchronously then run inline on Set*, keeping the race proofs
            // deterministic on the test thread.
            return new TaskCompletionSource<T>();
        }

        // ---- F4: cancellation/abandon versus completion has one terminal owner ----

        [Test]
        public void AbandonWins_CompletingLate_RejectsDeliveryExactlyOnce_ClaimNotStranded()
        {
            var source = Pending<PreparedActivityDelivery>();
            var lease = new OperationLease();

            Assert.IsTrue(ProviderOperations.AbandonPreparation(source.Task, lease, _provider),
                "the first abandon takes terminal ownership");
            Assert.IsFalse(lease.TryAdopt(), "processing cannot also claim ownership afterwards");

            // The provider completes LATE - after any hard drain ceiling would have
            // expired. The cleanup owner still resolves it deterministically.
            _provider.DebugAddSteps(600);
            var delivery = _provider.PreparePassiveDeliveryAsync(new ActivityCursor()).GetAwaiter().GetResult();
            source.SetResult(delivery);

            // Continuations registered with ExecuteSynchronously run inline on SetResult.
            var afterReject = _provider.PreparePassiveDeliveryAsync(new ActivityCursor()).GetAwaiter().GetResult();
            Assert.IsNotNull(afterReject?.snapshot,
                "the late delivery was rejected back to retryable pending, not consumed");
            Assert.AreEqual(600, afterReject.snapshot.stepCount);

            // A second rejection of the same delivery is a no-op: exactly-once resolution.
            ActivityTransactionCoordinator.RejectAbandonedPreparation(_provider, delivery);
            var again = _provider.PreparePassiveDeliveryAsync(new ActivityCursor()).GetAwaiter().GetResult();
            Assert.IsNull(again?.snapshot, "the identical window never duplicates");
        }

        [Test]
        public void AbandonWithFaultedTask_ObservesFault_WithoutStrandingAnything()
        {
            var source = Pending<PreparedActivityDelivery>();
            Exception observed = null;
            var lease = new OperationLease();

            Assert.IsTrue(ProviderOperations.AbandonPreparation(
                source.Task, lease, _provider, ex => observed = ex));

            source.SetException(new InvalidOperationException("provider exploded"));

            Assert.IsInstanceOf<InvalidOperationException>(observed,
                "the cleanup owner observes faults so nothing surfaces unobserved");
            Assert.IsNull(_provider.PreparePassiveDeliveryAsync(new ActivityCursor()).GetAwaiter().GetResult());
        }

        // ---- F3: passive timeout/cancel leaves movement retryable, no stranded claim --

        [Test]
        public void TimedOutThenLateDelivery_NextReconcileDeliversTheSameMovementOnce()
        {
            // Ticker sequence with a REAL staged window: prepare -> timeout/transfer ->
            // late completion -> the next reconcile delivers the identical window once.
            _provider.DebugAddSteps(1000);
            var real = _provider.PreparePassiveDeliveryAsync(new ActivityCursor()).GetAwaiter().GetResult();
            Assert.IsNotNull(real?.snapshot);

            var hung = Pending<PreparedActivityDelivery>();
            var lease = new OperationLease();
            ProviderOperations.AbandonPreparation(hung.Task, lease, _provider);

            // While the abandoned operation owns the staged window, overlapping
            // preparation returns null (single-open-claim rule), never a second claim.
            _provider.DebugAddSteps(50);
            Assert.IsNull(_provider.PreparePassiveDeliveryAsync(new ActivityCursor()).GetAwaiter().GetResult(),
                "an in-flight abandoned operation still owns its staged window");

            hung.SetResult(real); // the late completion arrives: rejected unprocessed

            var retry = _provider.PreparePassiveDeliveryAsync(new ActivityCursor()).GetAwaiter().GetResult();
            Assert.IsNotNull(retry?.snapshot, "after rejection the movement is immediately re-deliverable");
            Assert.AreEqual(1050, retry.snapshot.stepCount,
                "identical window plus newly folded steps, no loss, no duplication");
        }

        [Test]
        public void AbandonedStop_LateResult_ResolvesNonDurably_BaseMovementReturnsToPassive()
        {
            _provider.StartSessionAsync(SessionType.Walk).GetAwaiter().GetResult();
            _provider.SimulateSessionProgress(500, 0, 300);
            var result = _provider.StopSessionAsync().GetAwaiter().GetResult();
            Assert.AreEqual(500, result.acceptedSteps);

            // A hung stop observation abandons; its cleanup owner later receives the
            // (already completed here) result and resolves it NON-durably.
            var hungStop = Pending<ActivitySessionResult>();
            var lease = new OperationLease();
            Assert.IsTrue(ProviderOperations.AbandonSessionStop(hungStop.Task, lease, _provider));

            hungStop.SetResult(result);

            var recovery = _provider.PreparePassiveDeliveryAsync(new ActivityCursor()).GetAwaiter().GetResult();
            Assert.IsNotNull(recovery?.snapshot, "held base movement returned to the passive stream");
            Assert.AreEqual(500, recovery.snapshot.stepCount);
        }

        [Test]
        public void DiscardLateResult_ObservesFaults_AndToleratesCancellation()
        {
            var sampleSource = Pending<ActiveSessionSample>();
            var lease = new OperationLease();
            Exception observed = null;

            Assert.IsTrue(ProviderOperations.DiscardLateResult(sampleSource.Task, lease, ex => observed = ex));
            sampleSource.SetException(new ApplicationException("poll failed"));
            Assert.IsInstanceOf<ApplicationException>(observed);

            var canceledLease = new OperationLease();
            var canceled = Pending<ActiveSessionSample>();
            canceled.SetCanceled();
            Assert.DoesNotThrow(() =>
                ProviderOperations.DiscardLateResult(canceled.Task, canceledLease));
        }

        // ---- F8: start success + domain rejection aborts the provider session ------

        [Test]
        public void StartAdoptionFailure_AbortStopsSession_MovementReturnsToPassiveStream()
        {
            _provider.DebugAddSteps(120);
            Assert.AreEqual(SessionStartError.None,
                _provider.StartSessionAsync(SessionType.Walk).GetAwaiter().GetResult());

            // Domain rejects adoption (e.g. another canonical marker). The application
            // must explicitly abort the started session instead of leaking it.
            ActiveSessionAbort.Abort(_provider);

            var poll = _provider.PollSessionAsync().GetAwaiter().GetResult();
            Assert.IsFalse(poll.sessionActive, "no provider session remains running after abort");

            // The pre-existing passive window survived the aborted session untouched.
            var passive = _provider.PreparePassiveDeliveryAsync(new ActivityCursor()).GetAwaiter().GetResult();
            Assert.IsNotNull(passive?.snapshot);
            Assert.AreEqual(120, passive.snapshot.stepCount,
                "aborted-session cleanup restores movement without consuming or duplicating it");
        }
    }
}
