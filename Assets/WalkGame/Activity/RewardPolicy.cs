using System;

namespace WalkGame.Activity
{
    /// <summary>
    /// Tunable reward policy values (ACTIVITY_REWARD_SYSTEM.md). All values provisional
    /// until playtesting; kept in one place so balancing does not touch logic.
    /// </summary>
    public sealed class RewardPolicy
    {
        public static RewardPolicy Default { get; } = new RewardPolicy();

        // Base: 1 accepted step = 1 Vitality before caps (MASTER_PLAN 6).
        public long baseVitalityPerStep = 1;

        // Explorer: min(500, floor(distanceKm * 50)).
        public double explorerBonusPerKm = 50;
        public long explorerBonusCap = 500;

        // Endurance tiers on verified moving minutes.
        public double[] enduranceTierMinutes = { 20, 40, 60, 90 };
        public long[] enduranceTierAmounts = { 50, 100, 150, 200 };

        // Rhythm: sustained pedestrian cadence without extreme spikes.
        public double rhythmMinMovingMinutes = 10;
        public float rhythmConsistencyThreshold = 0.8f;
        public long rhythmBonusAmount = 50;

        // Tempo: flat award inside a broad sustained-run band; no per-km/h scaling.
        public double tempoMinSpeedKmh = 6.0;
        public double tempoMaxSpeedKmh = 16.0;
        public float tempoMinTrust = 0.8f;
        public long tempoBonusAmount = 100;
        public int tempoBonusesPerDayCap = 2;

        // Growth: personal improvement vs rolling baseline, at most once/day.
        public double growthImprovementThreshold = 0.05;
        public double growthImprovementCeiling = 0.15;
        public float growthMinTrust = 0.8f;
        public long growthBonusAmount = 75;

        // Session bonus cap applied to the sum of all bonus parts.
        public long sessionBonusCap = 750;

        // Trust bands.
        public float fullBonusTrustThreshold = 0.8f;
        public float reducedBonusTrustThreshold = 0.5f;
        public float reducedBonusMultiplier = 0.5f;

        // Plausibility bounds for pedestrian movement.
        public double maxPlausibleAverageSpeedKmh = 25.0;
        public double minStrideMeters = 0.3;
        public double maxStrideMeters = 1.8;
    }
}
