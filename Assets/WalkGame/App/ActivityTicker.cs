using System;
using System.Collections;
using UnityEngine;
using WalkGame.Activity;
using WalkGame.Core;
using WalkGame.World;

namespace WalkGame.App
{
    /// <summary>
    /// Drives the activity pipeline at gameplay cadence: passive snapshots on resume and
    /// periodically while playing, then persists. Also re-fires the processed event each
    /// poll so idle production and the HUD collect buttons stay live between snapshots.
    /// All reward logic stays in the domain; this is pure scheduling glue.
    /// </summary>
    public sealed class ActivityTicker : MonoBehaviour
    {
        private const float PollIntervalSeconds = 30f;

        public event Action ActivityProcessed;

        public void ProcessPassiveNow()
        {
            var host = GameHost.Current;
            if (host == null)
            {
                return;
            }

            var cursor = new ActivityCursor
            {
                lastSuccessfulSyncUtc = host.Profile.activityState.lastSuccessfulSyncUtc,
                providerCursor = host.Profile.activityState.providerCursor,
            };

            var snapshot = host.Provider.ReadSnapshotAsync(cursor).Result;
            if (snapshot != null)
            {
                host.Activity.ProcessPassiveSnapshot(snapshot);
                host.Profile.activityState.providerCursor = null; // debug provider keeps no extra cursor
                host.Persist();
            }

            // Fire even without a new snapshot: listeners refresh production/UI on cadence.
            ActivityProcessed?.Invoke();
        }

        public void CompleteDebugSession(SessionType type, long steps, double meters, double movingSeconds)
        {
            StartCoroutine(CompleteSessionRoutine(type, steps, meters, movingSeconds));
        }

        private IEnumerator CompleteSessionRoutine(SessionType type, long steps, double meters, double movingSeconds)
        {
            var host = GameHost.Current;
            if (host == null || !(host.Provider is DebugActivityProvider debug))
            {
                yield break;
            }

            var startError = debug.StartSessionAsync(type).Result;
            if (startError != SessionStartError.None)
            {
                yield break;
            }

            // Sessions are simulated instantly for the debug menu; a real Expedition UI
            // would accumulate samples over time instead (ROADMAP Phase 4C).
            debug.SimulateSessionProgress(steps, meters, movingSeconds);
            var result = debug.StopSessionAsync().Result;

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
            host.Persist();
            ActivityProcessed?.Invoke();
        }

        private void Start()
        {
            StartCoroutine(PollLoop());
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus && GameHost.Current != null)
            {
                ProcessPassiveNow();
                GameHost.Current.Production.AccrueAll(GameHost.Current.Profile.worldState.currentRegionId);
            }
        }

        private IEnumerator PollLoop()
        {
            var wait = new WaitForSecondsRealtime(PollIntervalSeconds);
            while (true)
            {
                yield return wait;
                if (GameHost.Current != null && Application.isFocused)
                {
                    ProcessPassiveNow();
                }
            }
        }
    }
}
