using System;
using WalkGame.Core;
using WalkGame.Persistence;

namespace WalkGame.Activity
{
    /// <summary>
    /// Engine-free orchestration of the activity transaction protocol (ADR 0010).
    /// Bridges domain mutation (ActivityService), provider resolution (IActivityProvider),
    /// and persistence outcome (PersistenceCoordinator) so a green headless suite means
    /// the real Unity completion/passive sequence is safe. Unity MonoBehaviours become
    /// thin wiring over this policy.
    ///
    /// Invariants (M8.4):
    /// - Provider/session reality and canonical activeSession cannot remain split-brain after rollback.
    /// - A failed Expedition reward commit must never leave a stale canonical marker that
    ///   permanently suppresses the provider's rejected base movement in the same process.
    /// - Retrying while storage is still failing remains safe: no double credit, no silent loss.
    /// - Fatal persistence loss fails closed; no fabricated reward or false durable acknowledgment.
    /// </summary>
    public sealed class ExpeditionCompletionReport
    {
        public ActivitySessionResult processedResult;
        public PersistenceCommitOutcome? commitOutcome;
        public bool providerResolved;
        public bool providerResolvedDurably;
        public bool repairedResurrectedMarker;
        public bool isFatal;
        public bool isDuplicateSession;
        public bool rewardCredited;
    }

    public sealed class PassiveDeliveryReport
    {
        public PassiveReconciliationResult reconciliationResult;
        public PersistenceCommitOutcome? commitOutcome;
        public bool providerResolved;
        public bool providerResolvedDurably;
        public bool isFatal;
    }

    /// <summary>
    /// Stateless policy: Unity callers pass collaborators and a commit delegate;
    /// this owns the ordering (process -> commit -> resolve -> repair) and the
    /// fatal-loss divergences so presentation never touches a dead profile.
    /// </summary>
    public static class ActivityTransactionCoordinator
    {
        /// <summary>
        /// Completes an Expedition session through the durable transaction protocol.
        /// Handles both the normal result path and the no-result stop-fault path (result==null).
        /// Trust is evaluated here so App stays thin and certifiable.
        /// </summary>
        public static ExpeditionCompletionReport CompleteExpedition(
            ActivityService activity,
            IActivityProvider provider,
            ActivitySessionResult result,
            Func<PersistenceCommitOutcome> commit,
            bool growthEligible = false)
        {
            if (activity == null) throw new ArgumentNullException(nameof(activity));
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            if (commit == null) throw new ArgumentNullException(nameof(commit));

            var report = new ExpeditionCompletionReport();

            // No-result path: the provider stop produced no usable result (fault/cancel/null).
            // The only canonical change is abandoning the active marker and durably closing it.
            if (result == null)
            {
                activity.AbandonExpedition();
                report.processedResult = null;
                report.isDuplicateSession = false;
                report.rewardCredited = false;

                var outcome = commit();
                report.commitOutcome = outcome;

                if (outcome == PersistenceCommitOutcome.Committed)
                {
                    report.providerResolved = false;
                    report.providerResolvedDurably = false;
                    report.isFatal = false;
                }
                else if (outcome == PersistenceCommitOutcome.RevertedToLastKnownGood)
                {
                    // Rollback may have resurrected the durable marker that AbandonExpedition just cleared.
                    // The controller knows the session ended, so same-process repair is safe: boot recovery
                    // remains the last-resort for process death before the repair converges durably.
                    if (activity.HasInterruptedSession)
                    {
                        activity.RecoverInterruptedSession();
                        report.repairedResurrectedMarker = true;
                    }
                    report.providerResolved = false;
                    report.isFatal = false;
                }
                else // FatalPersistenceLoss
                {
                    report.isFatal = true;
                    report.providerResolved = false;
                }

                return report;
            }

            // Normal result path: evaluate trust deterministically before domain processing.
            // This mirrors the pre-M8.4 controller ordering and makes the coordinator certifiable.
            var trust = new TrustEvaluator(RewardPolicy.Default);
            result.trustScore = trust.EvaluateSession(
                new ActiveSessionState
                {
                    accumulatedSteps = result.acceptedSteps,
                    accumulatedDistanceMeters = result.verifiedDistanceMeters,
                    movingSeconds = result.verifiedMovingSeconds,
                },
                hasLocationEvidence: false,
                mockLocationSuspected: false,
                teleportJump: false);

            long originalSteps = result.acceptedSteps;
            string sessionId = result.sessionId;

            var processed = activity.ProcessSessionResult(result, growthEligible);
            report.processedResult = processed;
            report.isDuplicateSession = processed != null && processed.acceptedSteps == 0 && originalSteps > 0;
            report.rewardCredited = processed != null && processed.acceptedSteps > 0;

            var commitOutcome = commit();
            report.commitOutcome = commitOutcome;

            if (commitOutcome == PersistenceCommitOutcome.Committed)
            {
                provider.ResolveSessionCompletion(sessionId, true);
                report.providerResolved = true;
                report.providerResolvedDurably = true;
                report.isFatal = false;
            }
            else if (commitOutcome == PersistenceCommitOutcome.RevertedToLastKnownGood)
            {
                provider.ResolveSessionCompletion(sessionId, false);
                report.providerResolved = true;
                report.providerResolvedDurably = false;
                report.isFatal = false;
                // ADR 0010: a failed commit reverts the profile in place from disk, which can restore
                // a previously autosaved activeSession marker. The provider just rejected completion so
                // its base movement returned to the passive stream, but the resurrected marker would
                // suppress that recovery in the same process. Repair the marker in memory; the repair
                // converges durably on the next successful commit and remains reconstructible via
                // boot recovery if the process dies before then.
                if (activity.HasInterruptedSession)
                {
                    activity.RecoverInterruptedSession();
                    report.repairedResurrectedMarker = true;
                }
            }
            else // FatalPersistenceLoss
            {
                report.isFatal = true;
                report.providerResolved = false;
                report.providerResolvedDurably = false;
                // Do NOT touch the provider: the runtime is being torn down and the provider instance
                // will be discarded with the host. Resolving against a dead provider would be meaningless
                // and could mutate orphaned state. Movement in this window is unrecoverable by design
                // (fail-closed, ADR 0007).
            }

            return report;
        }

        /// <summary>
        /// Delivers one prepared passive delivery through the durable transaction protocol.
        /// The caller must have already confirmed prepared?.snapshot != null.
        /// </summary>
        public static PassiveDeliveryReport DeliverPreparedPassive(
            ActivityService activity,
            IActivityProvider provider,
            PreparedActivityDelivery delivery,
            Func<PersistenceCommitOutcome> commit)
        {
            if (activity == null) throw new ArgumentNullException(nameof(activity));
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            if (delivery == null) throw new ArgumentNullException(nameof(delivery));
            if (delivery.snapshot == null) throw new ArgumentException("Delivery has no snapshot.", nameof(delivery));
            if (commit == null) throw new ArgumentNullException(nameof(commit));

            var report = new PassiveDeliveryReport();

            var outcome = activity.ProcessPassiveSnapshot(delivery.snapshot);
            report.reconciliationResult = outcome;

            if (!outcome.RequiresCommit)
            {
                bool durable = outcome.disposition != PassiveReconciliationDisposition.SuppressedBySession;
                provider.ResolvePreparedDelivery(delivery, durable);
                report.providerResolved = true;
                report.providerResolvedDurably = durable;
                report.commitOutcome = null;
                report.isFatal = false;
                return report;
            }

            var commitOutcome = commit();
            report.commitOutcome = commitOutcome;

            if (commitOutcome == PersistenceCommitOutcome.Committed)
            {
                provider.ResolvePreparedDelivery(delivery, true);
                report.providerResolved = true;
                report.providerResolvedDurably = true;
                report.isFatal = false;
            }
            else if (commitOutcome == PersistenceCommitOutcome.RevertedToLastKnownGood)
            {
                provider.ResolvePreparedDelivery(delivery, false);
                report.providerResolved = true;
                report.providerResolvedDurably = false;
                report.isFatal = false;
                // A failed passive commit reverts the live profile from disk, which can restore a
                // durable activeSession marker that was previously repaired in memory (e.g. after
                // an Expedition completion failure). Repair it here so the next passive retry is
                // not suppressed in the same process (ADR 0010). During a genuinely live
                // Expedition this branch is unreachable because suppressed deliveries never commit.
                if (activity.HasInterruptedSession)
                {
                    activity.RecoverInterruptedSession();
                }
            }
            else // FatalPersistenceLoss
            {
                report.isFatal = true;
                report.providerResolved = false;
                report.providerResolvedDurably = false;
            }

            return report;
        }

        /// <summary>
        /// Rejects a prepared delivery that arrived after a timeout without ever being processed.
        /// The snapshot's movement is returned to retryable pending state and the sync cursor
        /// remains untouched (fail-closed). Exactly the late-completion drain required by M8.4 D.
        /// </summary>
        public static void RejectAbandonedPreparation(IActivityProvider provider, PreparedActivityDelivery delivery)
        {
            if (provider == null) return;
            if (delivery == null) return;
            if (delivery.snapshot == null) return;
            provider.ResolvePreparedDelivery(delivery, false);
        }
    }
}
