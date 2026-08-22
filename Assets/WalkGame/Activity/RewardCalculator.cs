using System;
using WalkGame.Core;

namespace WalkGame.Activity
{
    /// <summary>
    /// Pure bonus computation (ACTIVITY_REWARD_SYSTEM sections 6-9). Deterministic and
    /// side-effect free so every rule is unit-testable without a provider or clock.
    /// </summary>
    public sealed class RewardCalculator
    {
        private readonly RewardPolicy _policy;

        public RewardCalculator(RewardPolicy policy)
        {
            _policy = policy ?? RewardPolicy.Default;
        }

        public long BaseVitality(long acceptedSteps)
        {
            return Math.Max(0, acceptedSteps) * _policy.baseVitalityPerStep;
        }

        public ActivityBonusBreakdown ComputeSessionBreakdown(
            long acceptedSteps,
            double verifiedDistanceMeters,
            double verifiedMovingSeconds,
            float? cadenceConsistency,
            float trustScore,
            bool classifiedSustainedRun,
            bool growthEligible)
        {
            var breakdown = new ActivityBonusBreakdown();

            // Trust gates the *optional* bonuses; base steps are handled by the caller.
            if (trustScore < _policy.reducedBonusTrustThreshold)
            {
                breakdown.totalBonus = 0;
                return breakdown;
            }

            double multiplier = trustScore >= _policy.fullBonusTrustThreshold ? 1.0 : _policy.reducedBonusMultiplier;

            breakdown.explorerBonus = ExplorerBonus(verifiedDistanceMeters);
            breakdown.enduranceBonus = EnduranceBonus(verifiedMovingSeconds);
            breakdown.rhythmBonus = RhythmBonus(verifiedMovingSeconds, cadenceConsistency);

            if (classifiedSustainedRun && trustScore >= _policy.tempoMinTrust)
            {
                breakdown.tempoBonus = _policy.tempoBonusAmount;
            }

            if (growthEligible && trustScore >= _policy.growthMinTrust)
            {
                breakdown.growthBonus = _policy.growthBonusAmount;
            }

            if (multiplier < 1.0)
            {
                breakdown.explorerBonus = ApplyMultiplier(breakdown.explorerBonus, multiplier);
                breakdown.enduranceBonus = ApplyMultiplier(breakdown.enduranceBonus, multiplier);
                breakdown.rhythmBonus = ApplyMultiplier(breakdown.rhythmBonus, multiplier);
                breakdown.tempoBonus = ApplyMultiplier(breakdown.tempoBonus, multiplier);
                breakdown.growthBonus = ApplyMultiplier(breakdown.growthBonus, multiplier);
            }

            long total = breakdown.SumParts();
            if (total > _policy.sessionBonusCap)
            {
                breakdown.capped = true;
                ScaleDownToCap(breakdown, total);
            }

            breakdown.totalBonus = breakdown.SumParts();
            return breakdown;
        }

        public long ExplorerBonus(double verifiedDistanceMeters)
        {
            if (verifiedDistanceMeters <= 0)
            {
                return 0;
            }

            double km = verifiedDistanceMeters / 1000.0;
            long bonus = (long)Math.Floor(km * _policy.explorerBonusPerKm);
            return Math.Min(bonus, _policy.explorerBonusCap);
        }

        public long EnduranceBonus(double verifiedMovingSeconds)
        {
            double minutes = verifiedMovingSeconds / 60.0;
            long best = 0;
            for (int i = 0; i < _policy.enduranceTierMinutes.Length && i < _policy.enduranceTierAmounts.Length; i++)
            {
                if (minutes >= _policy.enduranceTierMinutes[i])
                {
                    best = Math.Max(best, _policy.enduranceTierAmounts[i]);
                }
            }

            return best;
        }

        public long RhythmBonus(double verifiedMovingSeconds, float? cadenceConsistency)
        {
            double minutes = verifiedMovingSeconds / 60.0;
            if (minutes < _policy.rhythmMinMovingMinutes)
            {
                return 0;
            }

            if (!cadenceConsistency.HasValue || cadenceConsistency.Value < _policy.rhythmConsistencyThreshold)
            {
                return 0;
            }

            return _policy.rhythmBonusAmount;
        }

        /// <summary>Classifies a sustained run inside a broad band; never rewards raw top speed.</summary>
        public bool ClassifySustainedRun(double verifiedDistanceMeters, double verifiedMovingSeconds)
        {
            if (verifiedMovingSeconds <= 0 || verifiedDistanceMeters <= 0)
            {
                return false;
            }

            double kmh = (verifiedDistanceMeters / 1000.0) / (verifiedMovingSeconds / 3600.0);
            return kmh >= _policy.tempoMinSpeedKmh && kmh <= _policy.tempoMaxSpeedKmh;
        }

        /// <summary>Growth compares against the player's own recent baseline only.</summary>
        public bool IsGrowthEligible(double sessionDistanceMeters, double rollingBaselineDistanceMeters, string todayUtcDay, string lastGrowthBonusDayUtc)
        {
            if (!string.IsNullOrEmpty(lastGrowthBonusDayUtc) &&
                string.Equals(lastGrowthBonusDayUtc, todayUtcDay, StringComparison.Ordinal))
            {
                return false;
            }

            if (rollingBaselineDistanceMeters <= 0 || sessionDistanceMeters <= 0)
            {
                return false;
            }

            double improvement = (sessionDistanceMeters - rollingBaselineDistanceMeters) / rollingBaselineDistanceMeters;
            return improvement >= _policy.growthImprovementThreshold && improvement <= _policy.growthImprovementCeiling;
        }

        private static long ApplyMultiplier(long value, double multiplier)
        {
            return (long)Math.Floor(value * multiplier);
        }

        private void ScaleDownToCap(ActivityBonusBreakdown breakdown, long total)
        {
            double scale = (double)_policy.sessionBonusCap / Math.Max(1, total);
            breakdown.explorerBonus = ApplyMultiplier(breakdown.explorerBonus, scale);
            breakdown.enduranceBonus = ApplyMultiplier(breakdown.enduranceBonus, scale);
            breakdown.rhythmBonus = ApplyMultiplier(breakdown.rhythmBonus, scale);
            breakdown.tempoBonus = ApplyMultiplier(breakdown.tempoBonus, scale);
            breakdown.growthBonus = ApplyMultiplier(breakdown.growthBonus, scale);

            // Floor rounding loses a few units; fold the remainder into the largest
            // part so the capped total is exact and explainable.
            long remainder = _policy.sessionBonusCap - breakdown.SumParts();
            if (remainder > 0)
            {
                breakdown.explorerBonus += remainder;
            }
        }
    }
}
