#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Threading.Tasks;
using UnityEngine;
using WalkGame.Activity;
using WalkGame.Core;

namespace WalkGame.Platform.Android
{
    /// <summary>
    /// Thin adapter over com.walkgame.sensors.StepSensorBridge (Kotlin plugin).
    /// Native side returns sensor FACTS only; all deltas, baselines, trust and rewards
    /// are computed here or deeper in C# (AGENT_EXECUTION_GUIDE invariant 9).
    /// </summary>
    public sealed class AndroidStepSensorProvider : IActivityProvider
    {
        public const string ProviderIdValue = "activity.android.stepcounter";

        private readonly object _gate = new object();
        private readonly AndroidJavaObject _bridge;
        private readonly Log _log;

        private readonly IClock _clock;
        private double _lastRawCounter = double.MinValue;
        private long _pendingDelta;
        private ActiveSessionState _session;

        public AndroidStepSensorProvider(IClock clock, Log log = null)
        : this(clock, null, log)
        {
        }

        public AndroidStepSensorProvider(IClock clock, AndroidJavaObject existingBridge, Log log = null)
        {
            _log = log ?? Core.Log.Disabled;
            _clock = clock ?? Core.SystemClock.Instance;

            _bridge = existingBridge ?? new AndroidJavaObject("com.walkgame.sensors.StepSensorBridge");
            if (_bridge == null)
            {
                throw new InvalidOperationException("StepSensorBridge not resolvable.");
            }

            if (existingBridge == null)
            {
                // Hand the plugin the Unity player activity for permission prompts.
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                _bridge.Call("initialize", activity);
            }

            // Start receiving cumulative counter values while the app is in the foreground.
            // Reconciliation happens on app resume rather than a permanent background
            // service (MOBILE_ACTIVITY_INTEGRATION section 7).
            if (IsCounterAvailable())
            {
                _bridge.Call<bool>("startMonitoring");
            }
        }

        public string ProviderId => ProviderIdValue;

        public Task<ActivityCapability> GetCapabilityAsync()
        {
            bool available = IsCounterAvailable();
            var capability = new ActivityCapability
            {
                supportsPassiveSteps = available,
                supportsHistoricalQuery = false,
                supportsActiveSession = available,
                supportsDistance = false,
                supportsCadence = false,
                supportsLocationSession = false,
                motionPermission = (ActivityPermissionState)_bridge.Call<int>("getAuthorizationStatus"),
                locationPermission = ActivityPermissionState.Unavailable,
            };
            return Task.FromResult(capability);
        }

        /// <summary>Drains steps accumulated since the previous successful read.</summary>
        public Task<ActivitySnapshot> ReadSnapshotAsync(ActivityCursor cursor)
        {
            lock (_gate)
            {
                if (!IsCounterAvailable())
                {
                    return Task.FromResult<ActivitySnapshot>(null);
                }

                ConsumeRawCounter();

                if (_pendingDelta <= 0)
                {
                    return Task.FromResult<ActivitySnapshot>(null);
                }

                long steps = System.Math.Max(0, _pendingDelta);
                _pendingDelta = 0;

                DateTime end = _clock.UtcNow;
                var snapshot = new ActivitySnapshot
                {
                    providerId = ProviderId,
                    intervalStartUtc = cursor?.lastSuccessfulSyncUtc ?? end.AddMinutes(-30),
                    intervalEndUtc = end,
                    stepCount = steps,
                    estimatedDistanceMeters = null,
                    sourceType = ActivitySourceType.PhoneSensor,
                    recordingType = ActivityRecordingType.Passive,
                    quality = new ActivityQuality { hasStepEvidence = true },
                };
                snapshot.providerRecordIds.Add($"android.counter.{end.Ticks}");
                return Task.FromResult(snapshot);
            }
        }

        public Task<SessionStartError> StartSessionAsync(SessionType sessionType)
        {
            lock (_gate)
            {
                if (!IsCounterAvailable())
                {
                    return Task.FromResult(SessionStartError.SensorUnavailable);
                }

                if ((ActivityPermissionState)_bridge.Call<int>("getAuthorizationStatus") != ActivityPermissionState.Granted)
                {
                    return Task.FromResult(SessionStartError.PermissionDenied);
                }

                if (_session != null)
                {
                    return Task.FromResult(SessionStartError.AlreadyRunning);
                }

                ConsumeRawCounter();
                _session = new ActiveSessionState
                {
                    sessionType = sessionType,
                    startedAtUtc = _clock.UtcNow,
                    initialStepBaseline = (long)_lastRawCounter,
                };
                return Task.FromResult(SessionStartError.None);
            }
        }

        public Task<ActiveSessionSample> PollSessionAsync()
        {
            lock (_gate)
            {
                if (_session == null)
                {
                    return Task.FromResult(new ActiveSessionSample { sessionActive = false });
                }

                ConsumeRawCounter();
                long sessionSteps = _lastRawCounter > double.MinValue
                    ? (long)System.Math.Max(0, _lastRawCounter - _session.initialStepBaseline.GetValueOrDefault())
                    : 0;

                return Task.FromResult(new ActiveSessionSample
                {
                    sessionActive = true,
                    accumulatedSteps = sessionSteps,
                    accumulatedDistanceMeters = _session.accumulatedDistanceMeters,
                    movingSeconds = (_clock.UtcNow - _session.startedAtUtc).TotalSeconds,
                });
            }
        }

        public Task<ActivitySessionResult> StopSessionAsync()
        {
            ActiveSessionState finished;
            lock (_gate)
            {
                ConsumeRawCounter();
                finished = _session;
                _session = null;
            }

            if (finished == null)
            {
                return Task.FromResult<ActivitySessionResult>(null);
            }

            long steps = finished.HasBaseline && _lastRawCounter > double.MinValue
                ? (long)System.Math.Max(0, _lastRawCounter - finished.initialStepBaseline.GetValueOrDefault())
                : finished.accumulatedSteps;

            return Task.FromResult(new ActivitySessionResult
            {
                sessionId = finished.sessionId,
                type = finished.sessionType,
                startUtc = finished.startedAtUtc,
                endUtc = _clock.UtcNow,
                acceptedSteps = steps,
                verifiedDistanceMeters = 0, // distance requires the optional location flow (Phase 4C)
                verifiedMovingSeconds = (DateTime.UtcNow - finished.startedAtUtc).TotalSeconds,
                cadenceConsistency = null,
            });
        }

        private bool IsCounterAvailable()
        {
            try
            {
                return _bridge != null && _bridge.Call<bool>("isStepCounterAvailable");
            }
            catch (Exception ex)
            {
                _log.Warning($"Step counter availability check failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Reads the raw cumulative counter and folds it into the pending delta.
        /// A decreasing raw counter means reboot/provider reset: re-baseline, never
        /// produce negative steps (ACTIVITY_REWARD_SYSTEM section 16).
        /// </summary>
        private void ConsumeRawCounter()
        {
            double raw = _bridge.Call<double>("getCumulativeSteps");

            if (_lastRawCounter <= double.MinValue)
            {
                // First observation after install/process start: establish baseline.
                _lastRawCounter = raw;
                return;
            }

            if (raw >= _lastRawCounter)
            {
                double delta = raw - _lastRawCounter;
                _pendingDelta += (long)delta;
                _lastRawCounter = raw;
            }
            else
            {
                _log.Info("Step counter decreased; treating as reboot and re-baselining.");
                _lastRawCounter = raw;
            }
        }
    }
}
#endif
