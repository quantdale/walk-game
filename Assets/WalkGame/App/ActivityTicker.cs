using System;
using System.Collections;
using UnityEngine;
using WalkGame.Activity;
using WalkGame.Core;
using WalkGame.World;

namespace WalkGame.App
{
    /// <summary>
    /// Drives the activity pipeline at gameplay cadence. All potentially slow provider
    /// operations run as awaited tasks observed from coroutines - never through
    /// .Result/.Wait() on the Unity main thread (campaign S6):
    ///  - passive snapshots reconcile on resume and periodically while focused;
    ///  - an in-flight guard prevents overlapping reconciliations across fast
    ///    focus changes (a second trigger while busy is simply dropped);
    ///  - faults are caught, logged structurally, surfaced via LastReconcileFailed,
    ///    and never advance the sync cursor (cursor advances live in the domain);
    ///  - persistence happens only after successful state mutation, and the 30s
    ///    cadence never commits when reconciliation changed nothing durable while
    ///    every cursor/dedup/reward mutation still persists (ADR 0009);
    ///  - prepared provider deliveries are resolved exactly once against the
    ///    durability outcome: acknowledged on committed/duplicate-durable passes,
    ///    rejected back to retryable pending on suppression or failed saves.
    /// All reward logic stays in the domain; this is pure scheduling glue.
    /// </summary>
    public sealed class ActivityTicker : MonoBehaviour
    {
        private const float PollIntervalSeconds = 30f;
        private const float ReconcileTimeoutSeconds = 12f;

        public event Action ActivityProcessed;

        /// <summary>Diagnostics for UI/debug tooling: last reconcile ended in fault or timeout.</summary>
        public bool LastReconcileFailed { get; private set; }

        private bool _reconcileInFlight;

        public void ProcessPassiveNow()
        {
            var host = GameHost.Current;
            if (host == null || host.PersistenceBlocked)
            {
                // Blocked persistence health must not credit movement that cannot be
                // durably committed; the recovery screen owns the session instead.
                return;
            }

            if (_reconcileInFlight)
            {
                // A focus-change storm must not stack concurrent reconciliations over
                // one shared profile; the next poll cycle picks up anything new.
                return;
            }

            StartCoroutine(ReconcileRoutine());
        }

        private IEnumerator ReconcileRoutine()
        {
            var host = GameHost.Current;
            if (host == null)
            {
                yield break;
            }

            _reconcileInFlight = true;
            try
            {
                var cursor = new ActivityCursor
                {
                    lastSuccessfulSyncUtc = host.Profile.activityState.lastSuccessfulSyncUtc,
                    providerCursor = host.Profile.activityState.providerCursor,
                };

                var readTask = host.Provider.PreparePassiveDeliveryAsync(cursor);
                var readObservation = new TaskObservation<PreparedActivityDelivery>();
                var readObserver = TaskObservation.Observe(readTask, readObservation);
                float deadline = Time.realtimeSinceStartup + ReconcileTimeoutSeconds;
                while (!readObserver.IsCompleted && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                if (!readObserver.IsCompleted)
                {
                    // Timeout counts as a failed query: fail closed without touching
                    // durable state so the next successful window re-reads everything.
                    LastReconcileFailed = true;
                    GameHost.Current.Log.Warning("Passive snapshot read timed out; cursor untouched.");
                    yield break;
                }

                if (!HandleFault(readObservation))
                {
                    yield break;
                }

                var prepared = readObservation.Value;
                if (prepared?.snapshot != null)
                {
                    // ADR 0009: the delivery is processed against the canonical profile,
                    // then resolved exactly once against the proven durability outcome -
                    // acknowledge after a committed save (or a duplicate whose durable
                    // state already proves consumption), reject back to retryable pending
                    // on suppression or a failed/rolled-back save.
                    var outcome = host.Activity.ProcessPassiveSnapshot(prepared.snapshot);
                    bool resolvedDurably;
                    if (outcome.RequiresCommit)
                    {
                        // Domain credit + cursor advance mutate together; commit only
                        // after success - a failed write rolls the whole window back.
                        resolvedDurably = host.CommitChanges();
                        if (!resolvedDurably)
                        {
                            LastReconcileFailed = true;
                            host.Log.Warning("Activity commit failed; passive movement returned to the provider for retry.");
                        }
                    }
                    else
                    {
                        resolvedDurably = outcome.disposition != PassiveReconciliationDisposition.SuppressedBySession;
                    }

                    host.Provider.ResolvePreparedDelivery(prepared, resolvedDurably);
                }
            }
            finally
            {
                _reconcileInFlight = false;
                // Fire every pass (even suppressed/null reads) so idle production and
                // the HUD collect buttons refresh on cadence.
                ActivityProcessed?.Invoke();
            }
        }

        /// <summary>
        /// Completes a simulated Expedition session through the async provider surface
        /// without blocking the main thread; used by the debug menu only.
        /// </summary>
        public void CompleteDebugSession(SessionType type, long steps, double meters, double movingSeconds)
        {
            StartCoroutine(CompleteSessionRoutine(type, steps, meters, movingSeconds));
        }

        private IEnumerator CompleteSessionRoutine(SessionType type, long steps, double meters, double movingSeconds)
        {
            var host = GameHost.Current;
            if (host == null || host.PersistenceBlocked || !(host.Provider is DebugActivityProvider debug))
            {
                yield break;
            }

            var startTask = debug.StartSessionAsync(type);
            var startObservation = new TaskObservation<SessionStartError>();
            var startObserver = TaskObservation.Observe(startTask, startObservation);
            while (!startObserver.IsCompleted)
            {
                yield return null;
            }

            if (!HandleFault(startObservation) || startObservation.Value != SessionStartError.None)
            {
                yield break;
            }

            if (!host.Activity.BeginExpedition(type, host.Clock.UtcNow))
            {
                yield break;
            }

            // Sessions are simulated instantly for the debug menu; a real Expedition UI
            // would accumulate samples over time instead (ROADMAP Phase 4C).
            debug.SimulateSessionProgress(steps, meters, movingSeconds);

            var stopTask = debug.StopSessionAsync();
            var stopObservation = new TaskObservation<ActivitySessionResult>();
            var stopObserver = TaskObservation.Observe(stopTask, stopObservation);
            while (!stopObserver.IsCompleted)
            {
                yield return null;
            }

            if (!HandleFault(stopObservation))
            {
                host.Activity.AbandonExpedition();
                yield break;
            }

            var result = stopObservation.Value;
            if (result == null)
            {
                host.Activity.AbandonExpedition();
                yield break;
            }
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

            host.Activity.ProcessSessionResult(result, growthEligible: false);
            bool durable = host.CommitChanges();
            debug.ResolveSessionCompletion(result.sessionId, durable);
            ActivityProcessed?.Invoke();
        }

        /// <summary>Returns false when the task faulted or was canceled; logs and surfaces state.</summary>
        private bool HandleFault<T>(TaskObservation<T> observation)
        {
            if (observation.IsFaulted)
            {
                LastReconcileFailed = true;
                var inner = observation.Exception?.GetBaseException();
                GameHost.Current?.Log.Error($"Activity provider task faulted ({inner?.GetType().Name ?? observation.Exception?.GetType().Name}).");
                return false;
            }

            if (observation.IsCanceled)
            {
                LastReconcileFailed = true;
                GameHost.Current?.Log.Warning("Activity provider task was canceled.");
                return false;
            }

            LastReconcileFailed = false;
            return true;
        }

        private void Start()
        {
            StartCoroutine(PollLoop());
        }

        private IEnumerator PollLoop()
        {
            var wait = new WaitForSecondsRealtime(PollIntervalSeconds);
            while (true)
            {
                yield return wait;
                if (GameHost.Current != null && Application.isFocused && !GameHost.Current.PersistenceBlocked)
                {
                    ProcessPassiveNow();
                }
            }
        }
    }
}
