using System;
using System.Collections;
using UnityEngine;
using WalkGame.Activity;
using WalkGame.Core;
using WalkGame.Persistence;
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
    ///
    /// M8.5 ownership contract (ADR 0011): the 12s reconcile deadline is scheduling
    /// policy only. On expiry terminal ownership transfers atomically to a deterministic
    /// cleanup owner that survives this coroutine, so a late-completing preparation can
    /// NEVER strand a provider claim - it is rejected unprocessed whenever it arrives,
    /// keeping the same movement retryable on a later cycle. There is no hard cutoff
    /// after which a future completion becomes ownerless.
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

            // Capture the provider/activity instances before a potential fatal commit
            // tears them down (ADR 0010). The host object itself survives for logging.
            var provider = host.Provider;
            var activity = host.Activity;
            if (provider == null || activity == null)
            {
                yield break;
            }

            _reconcileInFlight = true;
            try
            {
                var cursor = new ActivityCursor
                {
                    lastSuccessfulSyncUtc = host.Profile?.activityState?.lastSuccessfulSyncUtc,
                    providerCursor = host.Profile?.activityState?.providerCursor,
                };

                var readTask = provider.PreparePassiveDeliveryAsync(cursor);
                var readObservation = new TaskObservation<PreparedActivityDelivery>();
                var readObserver = TaskObservation.Observe(readTask, readObservation);
                var readLease = new OperationLease();
                float deadline = Time.realtimeSinceStartup + ReconcileTimeoutSeconds;
                while (!readObserver.IsCompleted && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                if (!readObserver.IsCompleted)
                {
                    LastReconcileFailed = true;
                    // M8.5 runtime-ownership D: the deadline is scheduling policy, not a
                    // durability boundary. Transfer terminal ownership atomically to a
                    // cleanup owner that OUTLIVES this coroutine: whichever completes
                    // later wins exactly once. A late prepared delivery is rejected
                    // unprocessed (durable=false) so the staged claim returns to retryable
                    // pending, cursors stay untouched, and the next reconcile re-delivers
                    // the identical window. No completion timing can strand a claim.
                    if (ProviderOperations.AbandonPreparation(
                            readTask,
                            readLease,
                            provider,
                            ex => GameHost.Current?.Log.Error($"Late passive preparation faulted ({ex?.GetType().Name}).")))
                    {
                        host.Log.Warning("Passive snapshot read timed out; ownership transferred to the cleanup owner; cursor untouched.");
                        yield break;
                    }

                    // Abandon lost an exact-boundary race with completion; fall through
                    // and process the now-observed value under the normal protocol.
                }

                if (!HandleFault(readObservation))
                {
                    yield break;
                }

                // Exactly-one terminal owner: claim completion processing atomically so a
                // concurrent abandonment can never also resolve the same delivery.
                if (!readLease.TryAdopt())
                {
                    yield break;
                }

                var prepared = readObservation.Value;
                if (prepared?.snapshot != null)
                {
                    // ADR 0010: the transaction coordinator owns the full ordering
                    // (process -> commit -> resolve) so the headless suite certifies
                    // the real Unity sequence and fatal-loss convergence is safe.
                    var report = ActivityTransactionCoordinator.DeliverPreparedPassive(
                        activity,
                        provider,
                        prepared,
                        () => host.CommitChangesWithOutcome());
                    if (report.commitOutcome == PersistenceCommitOutcome.RevertedToLastKnownGood && !report.providerResolvedDurably)
                    {
                        LastReconcileFailed = true;
                        host.Log.Warning("Activity commit failed; passive movement returned to the provider for retry.");
                    }
                    else if (report.isFatal)
                    {
                        LastReconcileFailed = true;
                        host.Log.Error("Persistence fatal during passive reconciliation; entering blocked recovery.");
                    }
                    // Coordinator already resolved the provider; no further host.Provider
                    // dereference here, so a fatal transition cannot NRE (M8.4 audit fix).
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
                // M8.5 start-adoption rule: the started debug session must not leak.
                // Abort returns its simulated progress to the passive stream exactly once.
                ActiveSessionAbort.Abort(debug,
                    ex => host.Log?.Error($"Unadopted debug session abort failed ({ex?.GetType().Name})."));
                ActivityProcessed?.Invoke();
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

            // Capture activity before a potential fatal teardown (host survives for logging).
            var activityForSession = host.Activity;

            if (!HandleFault(stopObservation))
            {
                // Stop fault/cancel: durably close the marker through the coordinator
                // so a later revert cannot resurrect it and suppress passive recovery.
                ActivityTransactionCoordinator.CompleteExpedition(
                    activityForSession,
                    debug,
                    null,
                    () => host.CommitChangesWithOutcome());
                ActivityProcessed?.Invoke();
                yield break;
            }

            var result = stopObservation.Value;
            if (result == null)
            {
                ActivityTransactionCoordinator.CompleteExpedition(
                    activityForSession,
                    debug,
                    null,
                    () => host.CommitChangesWithOutcome());
                ActivityProcessed?.Invoke();
                yield break;
            }

            // ADR 0010: coordinator owns trust evaluation, credit, commit, resolve,
            // and post-rollback marker repair so this debug path matches the real
            // ExpeditionController transaction and stays headlessly certifiable.
            ActivityTransactionCoordinator.CompleteExpedition(
                activityForSession,
                debug,
                result,
                () => host.CommitChangesWithOutcome(),
                growthEligible: false);
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
