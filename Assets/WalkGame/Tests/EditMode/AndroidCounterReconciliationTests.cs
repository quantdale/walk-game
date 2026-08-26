using System;
using NUnit.Framework;
using WalkGame.Activity;

namespace WalkGame.Tests
{
    /// <summary>
    /// Campaign S5/S16 regression coverage for the Android TYPE_STEP_COUNTER folding
    /// state machine: unknown-start baselining, reboot resets, non-finite/negative
    /// payloads, repeated values and implausible jumps - every rule fails closed
    /// without corrupting state or generating negative/giant rewards.
    /// </summary>
    public sealed class AndroidCounterReconciliationTests
    {
        private AndroidCounterReconciler _reconciler;

        [SetUp]
        public void SetUp()
        {
            _reconciler = new AndroidCounterReconciler();
        }

        [Test]
        public void InitialStartup_NoSample_ThenFirstValid_EstablishesBaseline_NoReward()
        {
            // NaN = "nothing observed yet" from the native bridge.
            Assert.AreEqual(CounterFoldOutcome.InvalidSample, _reconciler.Fold(double.NaN));
            Assert.IsFalse(_reconciler.HasBaseline);
            Assert.AreEqual(0, _reconciler.PendingDelta);

            Assert.AreEqual(CounterFoldOutcome.BaselineEstablished, _reconciler.Fold(5000));
            Assert.IsTrue(_reconciler.HasBaseline);
            Assert.AreEqual(0, _reconciler.PendingDelta); // baseline never credits

            Assert.AreEqual(CounterFoldOutcome.DeltaCredited, _reconciler.Fold(5040));
            Assert.AreEqual(40, _reconciler.PendingDelta); // only the delta
        }

        [Test]
        public void RebootReset_Rebaselines_NoNegativeReward_ThenCreditsNewDelta()
        {
            _reconciler.Fold(5000);

            // Device rebooted; OS counter restarts near zero.
            Assert.AreEqual(CounterFoldOutcome.Rebaselined, _reconciler.Fold(20));
            Assert.AreEqual(0, _reconciler.PendingDelta);

            Assert.AreEqual(CounterFoldOutcome.DeltaCredited, _reconciler.Fold(120));
            Assert.AreEqual(100, _reconciler.PendingDelta);
        }

        [Test]
        public void InvalidSamples_NaN_Infinities_Negatives_FailClosed()
        {
            _reconciler.Fold(1000);

            foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1.0, -0.5 })
            {
                var outcome = _reconciler.Fold(invalid);
                Assert.AreEqual(CounterFoldOutcome.InvalidSample, outcome, $"sample {invalid}");
                Assert.AreEqual(1000.0, _reconciler.LastRawCounter.GetValueOrDefault(), $"baseline kept after {invalid}");
                Assert.AreEqual(0, _reconciler.PendingDelta, $"no delta after {invalid}");
            }
        }

        [Test]
        public void RepeatedIdenticalValues_ProduceNoCredit()
        {
            _reconciler.Fold(3000);

            Assert.AreEqual(CounterFoldOutcome.DeltaCredited, _reconciler.Fold(3000));
            Assert.AreEqual(0, _reconciler.PendingDelta);

            Assert.AreEqual(CounterFoldOutcome.DeltaCredited, _reconciler.Fold(3000));
            Assert.AreEqual(0, _reconciler.PendingDelta);
        }

        [Test]
        public void RepeatedRawSampleAfterDrain_DoesNotRecreditAcrossPollingCycles()
        {
            _reconciler.Fold(1000);
            _reconciler.Fold(1250);
            Assert.AreEqual(250, _reconciler.DrainPending());

            // A second passive poll can observe the same absolute counter after the
            // first delta was persisted and drained; it must not manufacture 250 more.
            Assert.AreEqual(CounterFoldOutcome.DeltaCredited, _reconciler.Fold(1250));
            Assert.AreEqual(0, _reconciler.PendingDelta);

            _reconciler.Fold(1300);
            Assert.AreEqual(50, _reconciler.PendingDelta);
        }

        [Test]
        public void ExtremelyLargeJump_IsTreatedAsCorruption_AndRebaselinesWithoutCredit()
        {
            _reconciler.Fold(2000);

            // A corrupted payload billions of steps high must never become Vitality.
            Assert.AreEqual(CounterFoldOutcome.AnomalyRebaselined, _reconciler.Fold(5.0e12));
            Assert.AreEqual(0, _reconciler.PendingDelta);
            Assert.AreEqual(5.0e12, _reconciler.LastRawCounter.GetValueOrDefault());

            // Normal walking continues cleanly from the new baseline.
            _reconciler.Fold(5.0e12 + 500);
            Assert.AreEqual(500, _reconciler.PendingDelta);
        }

        [Test]
        public void MultiDayCatchUp_WithinPlausibilityBound_CreditsFully()
        {
            _reconciler.Fold(100);

            // Three days of heavy walking while the app was closed.
            Assert.AreEqual(CounterFoldOutcome.DeltaCredited, _reconciler.Fold(100 + 90_000));
            Assert.AreEqual(90_000, _reconciler.PendingDelta);
        }

        [Test]
        public void DrainPending_ReturnsAndClearsAccumulatedDelta()
        {
            _reconciler.Fold(10);
            _reconciler.Fold(60);

            Assert.AreEqual(50, _reconciler.DrainPending());
            Assert.AreEqual(0, _reconciler.DrainPending());
            Assert.AreEqual(60.0, _reconciler.LastRawCounter.GetValueOrDefault());

            _reconciler.Fold(80);
            Assert.AreEqual(20, _reconciler.DrainPending());
        }

        // ---- ADR 0009 prepared-delivery claim semantics ------------------------

        [Test]
        public void ClaimPending_StagesMovement_WithoutDestroyingRetryability()
        {
            _reconciler.Fold(1000);
            _reconciler.Fold(1200);

            Assert.AreEqual(200, _reconciler.ClaimPending());
            Assert.IsTrue(_reconciler.HasOpenClaim);
            Assert.AreEqual(0, _reconciler.PendingDelta, "pending moved into the open claim");

            // Overlapping preparation cannot open a second claim over the same window.
            Assert.AreEqual(0, _reconciler.ClaimPending());
            Assert.IsTrue(_reconciler.HasOpenClaim);
        }

        [Test]
        public void AcknowledgeClaim_IsIdempotent_AndNeverRestores()
        {
            _reconciler.Fold(1000);
            _reconciler.Fold(1100);
            Assert.AreEqual(100, _reconciler.ClaimPending());

            _reconciler.AcknowledgeClaim();
            _reconciler.AcknowledgeClaim(); // repeated ack: safe no-op
            _reconciler.RestoreClaim();     // late reject after ack: must not resurrect

            Assert.IsFalse(_reconciler.HasOpenClaim);
            Assert.AreEqual(0, _reconciler.PendingDelta, "acknowledged movement never replays");
        }

        [Test]
        public void RestoreClaim_IsIdempotent_AndNeverGoesNegative()
        {
            _reconciler.Fold(1000);
            _reconciler.Fold(1300);
            Assert.AreEqual(300, _reconciler.ClaimPending());

            _reconciler.RestoreClaim();
            _reconciler.RestoreClaim(); // repeated reject: still exactly 300

            Assert.IsFalse(_reconciler.HasOpenClaim);
            Assert.AreEqual(300, _reconciler.PendingDelta);

            // The restored window is retryable exactly once.
            Assert.AreEqual(300, _reconciler.ClaimPending());
            _reconciler.RestoreClaim();
            Assert.AreEqual(300, _reconciler.PendingDelta);
        }

        [Test]
        public void RebootAfterRejectedClaim_KeepsLegitimatePending_NoHugeOrNegativeReward()
        {
            _reconciler.Fold(1000);
            _reconciler.Fold(1200);
            Assert.AreEqual(200, _reconciler.ClaimPending());
            _reconciler.RestoreClaim(); // commit failed; steps returned for retry

            // Device reboots before the retry: rebaseline, but the uncredited 200
            // remain legitimately pending and are delivered once, never doubled.
            Assert.AreEqual(CounterFoldOutcome.Rebaselined, _reconciler.Fold(30));
            Assert.AreEqual(200, _reconciler.PendingDelta);

            _reconciler.Fold(80);
            Assert.AreEqual(250, _reconciler.PendingDelta);
            Assert.AreEqual(250, _reconciler.ClaimPending());
            _reconciler.AcknowledgeClaim();
            Assert.AreEqual(0, _reconciler.PendingDelta);
        }

        [Test]
        public void ProcessRestartAfterRejectedDelivery_ReconstructsUncommittedWindowOnce()
        {
            // Durable cursor says raw=1000 (the rejected delivery never committed).
            var restarted = new AndroidCounterReconciler();
            restarted.SeedFromPersistedCounter(1000);

            // Live absolute counter includes the uncommitted window plus new walking.
            restarted.Fold(1250);
            Assert.AreEqual(250, restarted.PendingDelta,
                "the uncommitted window is reconstructed from the durable cursor exactly once");

            Assert.AreEqual(250, restarted.ClaimPending());
            restarted.AcknowledgeClaim(); // the retry's commit succeeds
            restarted.Fold(1300);
            Assert.AreEqual(50, restarted.PendingDelta, "only genuinely new movement remains");
        }

        [Test]
        public void SeedFromPersistedCursor_SameBootRestart_ResumesAndCreditsMissedWindow()
        {
            // Previous process persisted raw=4000 as its last credited cursor.
            var resumed = new AndroidCounterReconciler();
            resumed.SeedFromPersistedCounter(4000);
            Assert.IsTrue(resumed.HasBaseline);

            // Process restarted on the same boot; OS counter now at 4500.
            resumed.Fold(4500);
            Assert.AreEqual(500, resumed.PendingDelta, "steps between save and kill are credited exactly once");
        }

        [Test]
        public void SeedFromPersistedCursor_OldBoot_HigherLiveValueHandledAsReboot()
        {
            var resumed = new AndroidCounterReconciler();
            resumed.SeedFromPersistedCounter(90000);

            Assert.AreEqual(CounterFoldOutcome.Rebaselined, resumed.Fold(150));
            Assert.AreEqual(0, resumed.PendingDelta);

            resumed.Fold(250);
            Assert.AreEqual(100, resumed.PendingDelta);
        }

        [Test]
        public void SeedFromPersistedCursor_IgnoresNonFiniteJunk()
        {
            var resumed = new AndroidCounterReconciler();
            resumed.SeedFromPersistedCounter(double.NaN);
            resumed.SeedFromPersistedCounter(double.PositiveInfinity);

            Assert.IsFalse(resumed.HasBaseline);

            var outcome = resumed.Fold(700);
            Assert.AreEqual(CounterFoldOutcome.BaselineEstablished, outcome);
            Assert.AreEqual(700.0, resumed.LastRawCounter.GetValueOrDefault());
        }

        [Test]
        public void CustomCap_TightensAnomalyThreshold()
        {
            var strict = new AndroidCounterReconciler(maxPlausibleDelta: 1000);
            strict.Fold(500);

            Assert.AreEqual(CounterFoldOutcome.AnomalyRebaselined, strict.Fold(500 + 1001));
            Assert.AreEqual(0, strict.PendingDelta);

            Assert.AreEqual(CounterFoldOutcome.DeltaCredited, strict.Fold(500 + 1001 + 800));
            Assert.AreEqual(800, strict.PendingDelta);
        }

        [Test]
        public void Constructor_RejectsNonPositiveCap()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new AndroidCounterReconciler(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AndroidCounterReconciler(-5));
        }

        [Test]
        public void M84_RejectedExpedition_RestoredSteps_RetryExactlyOnce_WithBaselineAhead()
        {
            // Simulate the debug/Android completion-hold lifecycle: passive pending is
            // re-exposed after a rejected Expedition save via RestorePending.
            _reconciler.Fold(1000);
            _reconciler.Fold(1500); // +500 pending
            // Expedition held 300 of those as its completion claim (simulated via separate budget).
            // After a failed save that claim is restored to the passive stream.
            _reconciler.RestorePending(300);
            Assert.AreEqual(800, _reconciler.PendingDelta, "restored session steps plus uncredited passive window");

            // Runtime baseline stays ahead of the rolled-back persisted cursor; folding
            // from that baseline only credits increases, never duplicates the restored window.
            Assert.AreEqual(800, _reconciler.ClaimPending());
            _reconciler.RestoreClaim();
            Assert.AreEqual(800, _reconciler.PendingDelta, "rejected passive claim returns the exact restored window");
            Assert.AreEqual(800, _reconciler.ClaimPending());
            _reconciler.AcknowledgeClaim();
            Assert.AreEqual(0, _reconciler.PendingDelta, "acknowledged restored movement never replays");
        }
    }
}
