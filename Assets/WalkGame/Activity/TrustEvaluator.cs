using System;
using WalkGame.Core;

namespace WalkGame.Activity
{
    /// <summary>
    /// Plausibility/trust analysis (PRIVACY_SAFETY_ANTI_CHEAT 10). Produces a reward-quality
    /// score, never a punishment: low trust only limits optional bonuses; accepted steps
    /// always count. Not shown as an accusation anywhere in UX.
    /// </summary>
    public sealed class TrustEvaluator
    {
        private readonly RewardPolicy _policy;

        public TrustEvaluator(RewardPolicy policy)
        {
            _policy = policy ?? RewardPolicy.Default;
        }

        public float EvaluateSnapshot(ActivitySnapshot snapshot)
        {
            if (snapshot == null)
            {
                return 0f;
            }

            var quality = snapshot.quality;
            double score = 0.5; // neutral start

            if (quality.hasStepEvidence)
            {
                score += 0.25;
            }

            if (quality.hasCadenceEvidence)
            {
                score += 0.15;
            }

            if (quality.hasDistanceEvidence)
            {
                score += 0.05;
                if (!quality.hasStepEvidence && !quality.hasCadenceEvidence)
                {
                    quality.suspiciousFlags.Add(ActivitySuspicionFlag.NoStepEvidence);
                    score -= 0.35;
                }
            }

            // Distance must be compatible with the reported step count.
            if (snapshot.stepCount > 0 && quality.hasDistanceEvidence && snapshot.estimatedDistanceMeters.HasValue)
            {
                double stride = snapshot.estimatedDistanceMeters.Value / Math.Max(1, snapshot.stepCount);
                if (stride < _policy.minStrideMeters || stride > _policy.maxStrideMeters)
                {
                    quality.suspiciousFlags.Add(ActivitySuspicionFlag.VehicleLikeSpeed);
                    score -= 0.3;
                }
            }

            double hours = (snapshot.intervalEndUtc - snapshot.intervalStartUtc).TotalHours;
            if (hours > 0 && snapshot.stepCount > 0 && quality.hasDistanceEvidence && snapshot.estimatedDistanceMeters.HasValue)
            {
                double kmh = snapshot.estimatedDistanceMeters.Value / 1000.0 / hours;
                if (kmh > _policy.maxPlausibleAverageSpeedKmh)
                {
                    quality.suspiciousFlags.Add(ActivitySuspicionFlag.VehicleLikeSpeed);
                    score -= 0.4;
                }
            }

            if (quality.HasFlag(ActivitySuspicionFlag.TeleportJump))
            {
                score -= 0.2;
            }

            if (quality.HasFlag(ActivitySuspicionFlag.MockLocation))
            {
                score -= 0.3;
            }

            if (quality.HasFlag(ActivitySuspicionFlag.TimestampAnomaly))
            {
                score -= 0.15;
            }

            if (snapshot.recordingType == ActivityRecordingType.Manual ||
                quality.HasFlag(ActivitySuspicionFlag.ManualEntry))
            {
                score -= 0.3;
            }

            return (float)Math.Clamp(score, 0.0, 1.0);
        }

        /// <summary>
        /// Session-level trust combines evidence coherence with speed classification.
        /// Speed is used for validation/classification only, never as a reward multiplier.
        /// </summary>
        public float EvaluateSession(ActiveSessionState session, bool hasLocationEvidence, bool mockLocationSuspected, bool teleportJump)
        {
            if (session == null)
            {
                return 0f;
            }

            double score = 0.5;
            if (session.accumulatedSteps > 0)
            {
                score += 0.25;
            }

            if (session.movingSeconds > 60 && session.accumulatedSteps > 0)
            {
                score += 0.1;
            }

            if (hasLocationEvidence)
            {
                score += 0.1;
            }

            // Any meaningful distance must be commensurate with the reported steps;
            // a car ride yields absurd stride lengths even with a few sensor steps.
            if (session.accumulatedSteps > 0 && session.accumulatedDistanceMeters > 100)
            {
                double stride = session.accumulatedDistanceMeters / session.accumulatedSteps;
                if (stride < _policy.minStrideMeters || stride > _policy.maxStrideMeters)
                {
                    score -= 0.3;
                }
            }

            if (session.movingSeconds > 0 && session.accumulatedDistanceMeters > 0)
            {
                double kmh = (session.accumulatedDistanceMeters / 1000.0) / (session.movingSeconds / 3600.0);
                if (kmh > _policy.maxPlausibleAverageSpeedKmh)
                {
                    score -= 0.4;
                }
            }

            if (mockLocationSuspected)
            {
                score -= 0.3;
            }

            if (teleportJump)
            {
                score -= 0.2;
            }

            return (float)Math.Clamp(score, 0.0, 1.0);
        }
    }
}
