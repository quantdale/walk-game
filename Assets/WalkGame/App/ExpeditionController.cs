using System;
using System.Collections;
using UnityEngine;
using WalkGame.Activity;
using WalkGame.Core;

namespace WalkGame.App
{
    /// <summary>
    /// Player-facing Expedition lifecycle. It is the only runtime path that completes
    /// an active session: provider facts flow through ActivityService.ProcessSessionResult,
    /// so passive polling cannot create a second reward path.
    /// </summary>
    public sealed class ExpeditionController : MonoBehaviour
    {
        public event Action Changed;

        public bool IsActive { get; private set; }
        public bool IsBusy { get; private set; }
        public SessionType SessionType { get; private set; }
        public ActiveSessionSample LatestSample { get; private set; } = new ActiveSessionSample();
        public ActivitySessionResult LastResult { get; private set; }
        public string StatusMessage { get; private set; } = "No Expedition active";
        public string LastRewardMessage { get; private set; } = string.Empty;

        private bool _stopRequested;
        private bool _applicationPaused;

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
            if (host == null || host.Provider == null)
            {
                StatusMessage = "Movement tracking is unavailable on this device.";
                Changed?.Invoke();
                yield break;
            }

            IsBusy = true;
            SessionType = type;
            StatusMessage = type == SessionType.Run ? "Preparing Run Expedition…" : "Preparing Walk Expedition…";
            Changed?.Invoke();

            var startTask = host.Provider.StartSessionAsync(type);
            var startObservation = new TaskObservation<SessionStartError>();
            var startObserver = TaskObservation.Observe(startTask, startObservation);
            while (!startObserver.IsCompleted)
            {
                yield return null;
            }

            if (startObservation.IsFaulted || startObservation.IsCanceled ||
                startObservation.Value != SessionStartError.None)
            {
                IsBusy = false;
                StatusMessage = FriendlyStartFailure(startObservation.Value);
                Changed?.Invoke();
                yield break;
            }

            if (!host.Activity.BeginExpedition(type, host.Clock.UtcNow))
            {
                IsBusy = false;
                StatusMessage = "Another movement session is already active.";
                Changed?.Invoke();
                yield break;
            }

            IsBusy = false;
            IsActive = true;
            _stopRequested = false;
            LatestSample = new ActiveSessionSample { sessionActive = true };
            StatusMessage = type == SessionType.Run ? "Run Expedition active" : "Walk Expedition active";
            Changed?.Invoke();

            var pollWait = new WaitForSecondsRealtime(2f);
            while (!_stopRequested)
            {
                if (_applicationPaused)
                {
                    yield return null;
                    continue;
                }

                var pollTask = host.Provider.PollSessionAsync();
                var pollObservation = new TaskObservation<ActiveSessionSample>();
                var pollObserver = TaskObservation.Observe(pollTask, pollObservation);
                while (!pollObserver.IsCompleted)
                {
                    yield return null;
                }

                if (!pollObservation.IsFaulted && !pollObservation.IsCanceled && pollObservation.Value != null)
                {
                    LatestSample = pollObservation.Value;
                    Changed?.Invoke();
                }

                yield return pollWait;
            }

            IsBusy = true;
            var stopTask = host.Provider.StopSessionAsync();
            var stopObservation = new TaskObservation<ActivitySessionResult>();
            var stopObserver = TaskObservation.Observe(stopTask, stopObservation);
            while (!stopObserver.IsCompleted)
            {
                yield return null;
            }

            IsActive = false;
            IsBusy = false;
            host.Activity.AbandonExpedition();

            if (stopObservation.IsFaulted || stopObservation.IsCanceled || stopObservation.Value == null)
            {
                StatusMessage = "Expedition ended without a result; your passive steps remain safe.";
                Changed?.Invoke();
                yield break;
            }

            var result = stopObservation.Value;
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
            LastResult = host.Activity.ProcessSessionResult(result, growthEligible: false);
            LastRewardMessage = BuildRewardMessage(LastResult);
            // A failed commit reverts the session reward in place; the Changed refresh
            // below then presents the reverted (truthful) state instead of a phantom win.
            bool durable = host.CommitChanges();
            StatusMessage = durable ? "Expedition complete" : "Expedition finished, but it could not be saved";
            Changed?.Invoke();
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

        private static string BuildRewardMessage(ActivitySessionResult result)
        {
            if (result == null)
            {
                return string.Empty;
            }

            long bonus = result.bonusBreakdown?.totalBonus ?? 0;
            return $"+{result.acceptedSteps} steps → +{result.acceptedSteps + bonus} Vitality" +
                   (bonus > 0 ? $" ({bonus} activity bonus)" : string.Empty);
        }
    }
}
