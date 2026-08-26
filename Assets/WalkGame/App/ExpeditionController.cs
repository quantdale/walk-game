using System;
using System.Collections;
using UnityEngine;
using WalkGame.Activity;
using WalkGame.Core;
using WalkGame.Persistence;

namespace WalkGame.App
{
    /// <summary>
    /// Player-facing Expedition lifecycle over the sanctioned transaction protocol.
    /// Every completion - result, stop fault/cancel/null, or hung stop - delegates to
    /// <see cref="ActivityTransactionCoordinator"/>, the single authority for
    /// process -> commit -> provider resolve -> rollback-marker repair (ADR 0010/0011).
    ///
    /// Ownership rules (M8.5 runtime-ownership): provider operations observed here carry
    /// terminal-ownership leases, so a hang regains control on a bounded policy without
    /// blocking the main loop while a late completion stays owned exactly once. A provider
    /// start is provisional until the domain adopts the session; a rejected adoption
    /// explicitly aborts the started session so no native session leaks.
    /// </summary>
    public sealed class ExpeditionController : MonoBehaviour
    {
        public event Action Changed;

        /// <summary>Raised only after provider start AND domain adoption both succeed.</summary>
        public event Action StartConfirmed;

        public bool IsActive { get; private set; }
        public bool IsBusy { get; private set; }
        public SessionType SessionType { get; private set; }
        public ActiveSessionSample LatestSample { get; private set; } = new ActiveSessionSample();
        public ActivitySessionResult LastResult { get; private set; }
        public string StatusMessage { get; private set; } = "No Expedition active";
        public string LastRewardMessage { get; private set; } = string.Empty;

        // Scheduling policy bounds only - never a durability mechanism (ADR 0011).
        private const float OperationTimeoutSeconds = 10f;
        private const float StopTimeoutSeconds = 30f;
        private const float PollIntervalSeconds = 2f;

        private bool _stopRequested;
        private bool _applicationPaused;
        private IActivityProvider _providerOwningSession;

        public void StartExpedition(SessionType type)
        {
            var host = GameHost.Current;
            if (IsActive || IsBusy || host == null || host.PersistenceBlocked)
            {
                return;
            }

            StartCoroutine(RunExpedition(type));
        }

        public void FinishExpedition()
        {
            if (IsActive)
            {
                _stopRequested = true;
                StatusMessage = "Finishing safely…";
                Changed?.Invoke();
            }
        }

        public void SetStoppedForLifecycle()
        {
            if (!IsActive)
            {
                return;
            }

            StatusMessage = "Expedition paused — return to finish your session";
            Changed?.Invoke();
        }

        private IEnumerator RunExpedition(SessionType type)
        {
            var host = GameHost.Current;
            var provider = host?.Provider;
            if (host == null || provider == null)
            {
                StatusMessage = "Movement tracking is unavailable on this device.";
                Changed?.Invoke();
                yield break;
            }

            IsBusy = true;
            SessionType = type;
            StatusMessage = type == SessionType.Run ? "Preparing Run Expedition…" : "Preparing Walk Expedition…";
            Changed?.Invoke();

            // ---- start: bounded observation, adoption-gated -----------------------
            var startTask = provider.StartSessionAsync(type);
            var startObservation = new TaskObservation<SessionStartError>();
            var startObserver = TaskObservation.Observe(startTask, startObservation);
            float deadline = Time.realtimeSinceStartup + OperationTimeoutSeconds;
            while (!startObserver.IsCompleted && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            if (!startObserver.IsCompleted)
            {
                // Hung start: regain control without blocking the main loop; the late
                // result is observational (nothing canonical happened) and its cleanup
                // owner drops it harmlessly.
                ProviderOperations.DiscardLateResult(
                    startTask,
                    new OperationLease(),
                    ex => host.Log.Error($"Expedition start abandoned after fault ({ex?.GetType().Name})."));
                FinishBusy("The Expedition could not start. Your world is safe.");
                yield break;
            }

            if (startObservation.IsFaulted || startObservation.IsCanceled ||
                startObservation.Value != SessionStartError.None)
            {
                FinishBusy(FriendlyStartFailure(startObservation.Value));
                yield break;
            }

            if (!host.Activity.BeginExpedition(type, host.Clock.UtcNow))
            {
                // M8.5 start-adoption rule: provider success is provisional until the
                // domain accepts the session. Explicitly stop/abort it so its base
                // movement returns to the passive stream: no leak, no reward.
                ActiveSessionAbort.Abort(provider,
                    ex => host.Log.Error($"Unadopted Expedition session abort failed ({ex?.GetType().Name})."));
                FinishBusy("Another movement session is already active.");
                yield break;
            }

            IsBusy = false;
            IsActive = true;
            _stopRequested = false;
            _providerOwningSession = provider;
            LatestSample = new ActiveSessionSample { sessionActive = true };
            StatusMessage = type == SessionType.Run ? "Run Expedition active" : "Walk Expedition active";
            Changed?.Invoke();

            // Success cue fires only now: real provider start + real domain adoption
            // (durability-gated player truth, M8.5 G).
            StartConfirmed?.Invoke();

            // ---- poll: observational, bounded, generation-safe --------------------
            var pollWait = new WaitForSecondsRealtime(PollIntervalSeconds);
            while (!_stopRequested)
            {
                if (_applicationPaused)
                {
                    yield return null;
                    continue;
                }

                var pollTask = provider.PollSessionAsync();
                var pollObservation = new TaskObservation<ActiveSessionSample>();
                var pollObserver = TaskObservation.Observe(pollTask, pollObservation);
                deadline = Time.realtimeSinceStartup + OperationTimeoutSeconds;
                while (!pollObserver.IsCompleted && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                if (!pollObserver.IsCompleted)
                {
                    // Poll policy bound reached: skip this cycle's sample. Polling never
                    // mutates canonical state, and the late sample's owner discards it.
                    ProviderOperations.DiscardLateResult(pollTask, new OperationLease());
                }
                else if (!pollObservation.IsFaulted && !pollObservation.IsCanceled && pollObservation.Value != null)
                {
                    LatestSample = pollObservation.Value;
                    Changed?.Invoke();
                }

                yield return pollWait;
            }

            // ---- stop/completion: always through the shared protocol ---------------
            IsBusy = true;
            var stopTask = provider.StopSessionAsync();
            var stopObservation = new TaskObservation<ActivitySessionResult>();
            var stopObserver = TaskObservation.Observe(stopTask, stopObservation);
            var stopLease = new OperationLease();
            deadline = Time.realtimeSinceStartup + StopTimeoutSeconds;
            while (!stopObserver.IsCompleted && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            IsActive = false;
            IsBusy = false;
            _providerOwningSession = null;

            // Capture before a potential fatal commit tears down the host (ADR 0010).
            var activity = host.Activity;

            if (!stopObserver.IsCompleted)
            {
                // Hung stop policy: durably close the canonical marker through the shared
                // no-result protocol NOW, and transfer terminal ownership of the stop to
                // its cleanup owner - a late result resolves NON-durably so the session's
                // base movement returns to the passive stream exactly once.
                ProviderOperations.AbandonSessionStop(
                    stopTask,
                    stopLease,
                    provider,
                    ex => host.Log.Error($"Expedition stop abandoned after fault ({ex?.GetType().Name})."));
                ActivityTransactionCoordinator.CompleteExpedition(
                    activity,
                    provider,
                    null,
                    () => host.CommitChangesWithOutcome());
                LastRewardMessage = string.Empty;
                StatusMessage = "Expedition ended without a result; your passive steps remain safe.";
                Changed?.Invoke();
                yield break;
            }

            if (stopObservation.IsFaulted || stopObservation.IsCanceled || stopObservation.Value == null)
            {
                // No usable provider result: durably close the canonical marker through the
                // transaction coordinator so a later revert cannot resurrect it and suppress
                // the provider's returned base movement (M8.4 B / M8.5 F11).
                var emptyReport = ActivityTransactionCoordinator.CompleteExpedition(
                    activity,
                    provider,
                    null,
                    () => host.CommitChangesWithOutcome());
                LastRewardMessage = string.Empty;
                StatusMessage = emptyReport.isFatal
                    ? "Expedition ended without a durable save; recovery mode is active"
                    : "Expedition ended without a result; your passive steps remain safe.";
                Changed?.Invoke();
                yield break;
            }

            var report = ActivityTransactionCoordinator.CompleteExpedition(
                activity,
                provider,
                stopObservation.Value,
                () => host.CommitChangesWithOutcome(),
                growthEligible: false);
            LastResult = report.processedResult;
            // Durability-gated truth (M8.5 G): positive reward copy exists ONLY for a
            // proven committed save; reverted/fatal outcomes show truthful copy instead
            // of a phantom win.
            LastRewardMessage = ExpeditionResultPresentation.RewardSummary(report);
            StatusMessage = ExpeditionResultPresentation.CompletionStatus(report);
            Changed?.Invoke();
        }

        private void FinishBusy(string message)
        {
            IsBusy = false;
            StatusMessage = message;
            Changed?.Invoke();
        }

        private void OnDestroy()
        {
            // Runtime-generation safety (M8.5 E): if this controller dies while its
            // provider session may still be live outside a sanctioned shutdown path,
            // abort it so no native session leaks into a replacement runtime. Harmless
            // when the session already ended or the provider was already shut down
            // (post-shutdown stops are benign no-ops).
            var provider = _providerOwningSession;
            if (provider != null)
            {
                ActiveSessionAbort.Abort(provider);
                _providerOwningSession = null;
            }
        }

        private void OnApplicationPause(bool paused)
        {
            _applicationPaused = paused;
            if (!IsActive)
            {
                return;
            }

            StatusMessage = paused
                ? "Expedition paused safely — finish it when you return"
                : "Expedition resumed — movement tracking is live";
            Changed?.Invoke();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused)
            {
                OnApplicationPause(true);
            }
            else if (_applicationPaused)
            {
                OnApplicationPause(false);
            }
        }

        private static string FriendlyStartFailure(SessionStartError error)
        {
            switch (error)
            {
                case SessionStartError.PermissionDenied:
                    return "Motion access is off. You can still build and explore; enable it to start an Expedition.";
                case SessionStartError.SensorUnavailable:
                    return "This device cannot provide live movement tracking right now.";
                case SessionStartError.AlreadyRunning:
                    return "Another movement session is already active.";
                default:
                    return "The Expedition could not start. Your world is safe.";
            }
        }
    }
}
