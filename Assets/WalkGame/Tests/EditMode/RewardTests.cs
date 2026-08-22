using System;
using NUnit.Framework;
using WalkGame.Activity;
using WalkGame.Core;

namespace WalkGame.Tests
{
    public sealed class RewardCalculatorTests
    {
        private RewardPolicy _policy;
        private RewardCalculator _calculator;

        [SetUp]
        public void SetUp()
        {
            _policy = RewardPolicy.Default;
            _calculator = new RewardCalculator(_policy);
        }

        [Test]
        public void BaseVitality_IsOnePerStep()
        {
            Assert.AreEqual(1000, _calculator.BaseVitality(1000));
            Assert.AreEqual(0, _calculator.BaseVitality(0));
        }

        [Test]
        public void ExplorerBonus_ScalesWithDistance_AndCaps()
        {
            // 2 km -> 100
            Assert.AreEqual(100, _calculator.ExplorerBonus(2000));
            // 10 km -> 500 (cap)
            Assert.AreEqual(500, _calculator.ExplorerBonus(10000));
            // 42 km -> still capped at 500
            Assert.AreEqual(500, _calculator.ExplorerBonus(42000));
        }

        [Test]
        public void EnduranceBonus_UsesHighestReachedTier()
        {
            Assert.AreEqual(0, _calculator.EnduranceBonus(0));
            Assert.AreEqual(50, _calculator.EnduranceBonus(20 * 60));
            Assert.AreEqual(150, _calculator.EnduranceBonus(60 * 60));
            Assert.AreEqual(200, _calculator.EnduranceBonus(120 * 60)); // beyond top tier: cap value holds
        }

        [Test]
        public void RhythmBonus_RequiresMovingMinutes_AndCadenceConsistency()
        {
            Assert.AreEqual(0, _calculator.RhythmBonus(5 * 60, 0.95f));
            Assert.AreEqual(0, _calculator.RhythmBonus(15 * 60, null));
            Assert.AreEqual(0, _calculator.RhythmBonus(15 * 60, 0.5f));
            Assert.AreEqual(50, _calculator.RhythmBonus(15 * 60, 0.9f));
        }

        [Test]
        public void SustainedRunClassification_FlatBand_NoSpeedScaling()
        {
            // 12 km/h for one hour -> inside band.
            Assert.IsTrue(_calculator.ClassifySustainedRun(12000, 3600));
            // 40 km/h vehicle-like -> outside band.
            Assert.IsFalse(_calculator.ClassifySustainedRun(40000, 3600));
            // 3 km/h stroll -> not a run.
            Assert.IsFalse(_calculator.ClassifySustainedRun(3000, 3600));
        }

        [Test]
        public void SessionBreakdown_TotalIsCapped()
        {
            var breakdown = _calculator.ComputeSessionBreakdown(
                acceptedSteps: 20000,
                verifiedDistanceMeters: 40000,   // explorer 500 (capped internally)
                verifiedMovingSeconds: 4 * 3600, // endurance 200
                cadenceConsistency: 0.9f,        // rhythm 50
                trustScore: 1.0f,
                classifiedSustainedRun: true,    // tempo 100
                growthEligible: true);           // growth 75

            long parts = breakdown.SumParts();
            Assert.IsTrue(breakdown.capped, "cap flag must be set");
            Assert.AreEqual(_policy.sessionBonusCap, parts);
            Assert.AreEqual(_policy.sessionBonusCap, breakdown.totalBonus);
        }

        [Test]
        public void SessionBreakdown_LowTrust_GrantsNoBonus()
        {
            var breakdown = _calculator.ComputeSessionBreakdown(
                acceptedSteps: 5000,
                verifiedDistanceMeters: 5000,
                verifiedMovingSeconds: 1800,
                cadenceConsistency: 0.95f,
                trustScore: 0.3f,
                classifiedSustainedRun: false,
                growthEligible: true);

            Assert.AreEqual(0, breakdown.totalBonus);
        }

        [Test]
        public void SessionBreakdown_MediumTrust_ReducesBonuses()
        {
            var full = _calculator.ComputeSessionBreakdown(0, 2000, 30 * 60, 0.9f, 1.0f, false, false);
            var medium = _calculator.ComputeSessionBreakdown(0, 2000, 30 * 60, 0.9f, 0.6f, false, false);

            Assert.Greater(full.explorerBonus, 0);
            Assert.AreEqual((long)(full.explorerBonus * 0.5), medium.explorerBonus);
        }

        [Test]
        public void GrowthEligibility_CompareToOwnBaseline_OncePerDay()
        {
            bool eligible = _calculator.IsGrowthEligible(
                sessionDistanceMeters: 5500,
                rollingBaselineDistanceMeters: 5000,
                todayUtcDay: "2026-01-01",
                lastGrowthBonusDayUtc: "2025-12-31");
            Assert.IsTrue(eligible);

            bool sameDay = _calculator.IsGrowthEligible(5500, 5000, "2026-01-01", "2026-01-01");
            Assert.IsFalse(sameDay);

            bool tooBigJump = _calculator.IsGrowthEligible(9000, 5000, "2026-01-01", "2025-12-31");
            Assert.IsFalse(tooBigJump);
        }
    }

    public sealed class TrustEvaluatorTests
    {
        private TrustEvaluator _evaluator;

        [SetUp]
        public void SetUp()
        {
            _evaluator = new TrustEvaluator(RewardPolicy.Default);
        }

        [Test]
        public void CoherentPedestrianSnapshot_EarnsHighTrust()
        {
            var snapshot = new ActivitySnapshot
            {
                stepCount = 8000,
                estimatedDistanceMeters = 6000, // stride ~0.75m: plausible walking
                intervalStartUtc = DateTime.UtcNow.AddHours(-1),
                intervalEndUtc = DateTime.UtcNow,
                quality = new ActivityQuality
                {
                    hasStepEvidence = true,
                    hasDistanceEvidence = true,
                    hasCadenceEvidence = true,
                },
            };

            float trust = _evaluator.EvaluateSnapshot(snapshot);
            Assert.GreaterOrEqual(trust, 0.8f);
        }

        [Test]
        public void DistanceWithNoStepEvidence_IsSuspicious()
        {
            var snapshot = new ActivitySnapshot
            {
                stepCount = 0,
                estimatedDistanceMeters = 50000,
                intervalStartUtc = DateTime.UtcNow.AddMinutes(-30),
                intervalEndUtc = DateTime.UtcNow,
                quality = new ActivityQuality
                {
                    hasStepEvidence = false,
                    hasDistanceEvidence = true,
                    hasLocationEvidence = true,
                },
            };

            float trust = _evaluator.EvaluateSnapshot(snapshot);
            Assert.Less(trust, 0.5f);
            Assert.IsTrue(snapshot.quality.HasFlag(ActivitySuspicionFlag.NoStepEvidence));
        }

        [Test]
        public void VehicleLikeSpeed_IsFlagged_AndDropsTrust()
        {
            var snapshot = new ActivitySnapshot
            {
                stepCount = 500,
                estimatedDistanceMeters = 60000, // 60 km in 1h with 500 steps
                intervalStartUtc = DateTime.UtcNow.AddHours(-1),
                intervalEndUtc = DateTime.UtcNow,
                quality = new ActivityQuality
                {
                    hasStepEvidence = true,
                    hasDistanceEvidence = true,
                },
            };

            float trust = _evaluator.EvaluateSnapshot(snapshot);
            Assert.Less(trust, 0.5f);
            Assert.IsTrue(snapshot.quality.HasFlag(ActivitySuspicionFlag.VehicleLikeSpeed));
        }

        [Test]
        public void ManualEntry_DropsTrust_BelowFullBonus()
        {
            var snapshot = new ActivitySnapshot
            {
                stepCount = 4000,
                intervalStartUtc = DateTime.UtcNow.AddHours(-1),
                intervalEndUtc = DateTime.UtcNow,
                recordingType = ActivityRecordingType.Manual,
                quality = new ActivityQuality { hasStepEvidence = true },
            };

            float trust = _evaluator.EvaluateSnapshot(snapshot);
            Assert.Less(trust, 0.8f);
        }
    }
}
