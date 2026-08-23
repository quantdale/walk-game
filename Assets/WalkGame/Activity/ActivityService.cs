using System;
using WalkGame.Core;

namespace WalkGame.Activity
{
    /// <summary>
    /// Normalization pipeline host (TECHNICAL_ARCHITECTURE 9):
    /// provider data -> dedup -> trust -> reward -> VitalityLedger credit,
    /// then sync cursor advances together with the credit so a crash between
    /// the two cannot duplicate rewards (AGENT_EXECUTION_GUIDE 12).
    /// Persistence of the mutated profile happens in the same save cycle.
    /// </summary>
    public sealed class ActivityService
    {
        private readonly PlayerProfile _profile;
        private readonly VitalityLedger _ledger;
        private readonly TrustEvaluator _trust;
        private readonly RewardCalculator _rewards;
        private readonly DomainEvents _events;
        private readonly Log _log;

        public ActivityService(
            PlayerProfile profile,
            VitalityLedger ledger,
            TrustEvaluator trust,
            RewardCalculator rewards,
            DomainEvents events,
            Log log)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            _trust = trust ?? throw new ArgumentNullException(nameof(trust));
            _rewards = rewards ?? throw new ArgumentNullException(nameof(rewards));
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _log = log ?? Log.Disabled;
        }

        /// <summary>
        /// Processes one passive provider snapshot. Passive movement earns base Vitality
        /// only - never speed/route bonuses (ACTIVITY_REWARD_SYSTEM 4). Returns accepted steps.
        /// Exactly-once policy (campaign S8): while an Expedition is active the passive
        /// stream is suppressed entirely - both pathways would otherwise claim the same
        /// physical steps; the active session owns its window and passive windows resume
        /// after its end. Suppressed reads do not touch dedup keys or cursors.
        /// </summary>
        public long ProcessPassiveSnapshot(ActivitySnapshot snapshot)
        {
            if (snapshot == null)
            {
                return 0;
            }

            if (_profile.activityState.activeSession != null)
            {
                _log.Debug("Passive snapshot suppressed: Expedition in progress owns the movement window.");
                return 0;
            }

            string dedupKey = snapshot.IntervalDedupKey();
            if (!_profile.activityState.creditedIntervals.TryMarkCredited(dedupKey))
            {
                _log.Debug($"Passive interval already credited: {dedupKey}");
                AdvanceCursor(snapshot);
                return 0;
            }

            long acceptedSteps = Math.Max(0, snapshot.stepCount);
            if (acceptedSteps > 0)
            {
                _ledger.Credit(VitalityCredit.Steps(_rewards.BaseVitality(acceptedSteps)));
                _profile.lifetimeAcceptedSteps += acceptedSteps;
            }

            if (snapshot.estimatedDistanceMeters.HasValue && snapshot.estimatedDistanceMeters.Value > 0)
            {
                _profile.lifetimeVerifiedDistanceMeters += snapshot.estimatedDistanceMeters.Value;
            }

            AdvanceCursor(snapshot);
            CheckStepMilestones();
            return acceptedSteps;
        }

        /// <summary>
        /// Claims the live movement window for an Expedition after the provider has
        /// successfully started. This domain-owned marker prevents a lifecycle poll
        /// from paying the same movement passively while the session is active.
        /// </summary>
        public bool BeginExpedition(SessionType sessionType, DateTime startedAtUtc)
        {
            if (_profile.activityState.activeSession != null)
            {
                return false;
            }

            _profile.activityState.activeSession = new ActiveSessionState
            {
                sessionType = sessionType,
                startedAtUtc = startedAtUtc,
            };
            return true;
        }

        public void AbandonExpedition()
        {
            _profile.activityState.activeSession = null;
        }

        /// <summary>
        /// True while a persisted Expedition marker exists. A provider session never
        /// survives process death, so a marker seen at composition is stale by
        /// definition and would suppress every future passive read forever.
        /// </summary>
        public bool HasInterruptedSession => _profile.activityState.activeSession != null;

        /// <summary>
        /// Clears a stale Expedition marker left by a process kill mid-session
        /// (M8 red-team). Recovery credits nothing itself: the interrupted session's
        /// result was never delivered, and movement made during the interruption is
        /// re-read from the provider cursor through normal passive reconciliation,
        /// so credit stays exactly-once. Idempotent.
        /// </summary>
        public bool RecoverInterruptedSession()
        {
            if (_profile.activityState.activeSession == null)
            {
                return false;
            }

            _log.Info("Recovered from an interrupted Expedition; passive movement credit resumes.");
            _profile.activityState.activeSession = null;
            return true;
        }

        /// <summary>
        /// Processes a completed Expedition. Base steps always count; optional bonuses are
        /// gated by trust and capped. Low trust is communicated neutrally by UI, not punished.
        /// Exactly-once policy (campaign S8): a durable per-session identity prevents any
        /// re-delivery of the same result from paying twice, and on first credit the sync
        /// cursor jumps to the session end so later passive windows cannot re-read the
        /// same physical steps through historical queries (iOS path).
        /// </summary>
        public ActivitySessionResult ProcessSessionResult(ActivitySessionResult result, bool growthEligible)
        {
            if (result == null)
            {
                return null;
            }

            string sessionKey = $"session:{result.sessionId}";
            if (!_profile.activityState.creditedSessionIds.TryMarkCredited(sessionKey))
            {
                _log.Info($"Duplicate expedition result ignored: {sessionKey}");
                result.acceptedSteps = 0;
                result.bonusBreakdown = new ActivityBonusBreakdown();
                return result;
            }

            long acceptedSteps = Math.Max(0, result.acceptedSteps);
            if (acceptedSteps > 0)
            {
                _ledger.Credit(VitalityCredit.Steps(_rewards.BaseVitality(acceptedSteps)));
                _profile.lifetimeAcceptedSteps += acceptedSteps;
            }

            if (result.verifiedDistanceMeters > 0)
            {
                _profile.lifetimeVerifiedDistanceMeters += result.verifiedDistanceMeters;
            }

            var breakdown = _rewards.ComputeSessionBreakdown(
                acceptedSteps,
                result.verifiedDistanceMeters,
                result.verifiedMovingSeconds,
                result.cadenceConsistency,
                result.trustScore,
                classifiedSustainedRun: _rewards.ClassifySustainedRun(result.verifiedDistanceMeters, result.verifiedMovingSeconds),
                growthEligible: growthEligible);

            result.bonusBreakdown = breakdown;

            if (_profile.lifetimeAcceptedSteps > 0 && breakdown.totalBonus > 0)
            {
                CreditBreakdown(result.sessionId, breakdown);
            }

            // Session completed; clear active session state only after crediting succeeded,
            // then partition the passive timeline past this window.
            _profile.activityState.activeSession = null;
            if (result.endUtc > (_profile.activityState.lastSuccessfulSyncUtc ?? DateTime.MinValue))
            {
                _profile.activityState.lastSuccessfulSyncUtc = result.endUtc;
            }

            CheckStepMilestones();
            return result;
        }

        private void CreditBreakdown(string sessionId, ActivityBonusBreakdown breakdown)
        {
            CreditPart(breakdown.explorerBonus, WellKnownIds.ReasonCodes.ExplorerBonus, sessionId);
            CreditPart(breakdown.enduranceBonus, WellKnownIds.ReasonCodes.EnduranceBonus, sessionId);
            CreditPart(breakdown.rhythmBonus, WellKnownIds.ReasonCodes.RhythmBonus, sessionId);
            CreditPart(breakdown.tempoBonus, WellKnownIds.ReasonCodes.TempoBonus, sessionId);
            CreditPart(breakdown.growthBonus, WellKnownIds.ReasonCodes.GrowthBonus, sessionId);

            if (breakdown.capped)
            {
                _log.Info("Session bonus capped by policy.");
            }
        }

        private void CreditPart(long amount, string reasonCode, string relatedEntityId)
        {
            if (amount <= 0)
            {
                return;
            }

            _ledger.Credit(new VitalityCredit { amount = amount, reasonCode = reasonCode, relatedEntityId = relatedEntityId });
        }

        private void AdvanceCursor(ActivitySnapshot snapshot)
        {
            if (snapshot.intervalEndUtc > (_profile.activityState.lastSuccessfulSyncUtc ?? DateTime.MinValue))
            {
                _profile.activityState.lastSuccessfulSyncUtc = snapshot.intervalEndUtc;
            }
        }

        private void CheckStepMilestones()
        {
            MilestonesPending?.Invoke(_profile);
        }

        /// <summary>
        /// Hook invoked whenever lifetime steps change; the milestone service subscribes
        /// rather than ActivityService depending on it directly.
        /// </summary>
        public event Action<PlayerProfile> MilestonesPending;
    }
}
