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
    /// Native side returns sensor FACTS and authorization state only; all deltas,
    /// baselines, trust and rewards are computed here or deeper in C#
    /// (AGENT_EXECUTION_GUIDE invariant 9).
    ///
    /// Lifecycle contract:
    ///  - Construction never fails because of missing permission - only an
    ///    unresolvable bridge (packaging bug) throws, which GameHost treats as
    ///    infrastructure failure rather than a user choice.
    ///  - The step-counter listener is registered lazily once ACTIVITY_RECOGNITION
    ///    is granted; registering earlier would only observe zeroed values on
    ///    Android 10+ and waste the first valid baseline.
    ///  - Permission prompts fire exclusively through RequestMotionPermissionAsync,
    ///    which the UI invokes after explicit user intent (PRIVACY_SAFETY_ANTI_CHEAT 4).
    /// </summary>
    public sealed class AndroidStepSensorProvider : IActivityProvider
    {
        public const string ProviderIdValue = "activity.android.stepcounter";

        private static readonly TimeSpan RequestPollTimeout = TimeSpan.FromSeconds(120);
        private const int RequestPollIntervalMs = 300;

        private readonly object _gate = new object();
        private readonly AndroidJavaObject _bridge;
        private readonly AndroidJavaObject _activity;
        private readonly Log _log;

        private readonly IClock _clock;
        private double _lastRawCounter = double.MinValue;
        private long _pendingDelta;
        private ActiveSessionState _session;

        // Runtime refinement over the native tri-state: once a request round has
        // completed without grant, later non-granted reports mean "denied", not
        // "never asked" (Android 11+ stops showing the dialog entirely after
        // repeated denials, so the raw status alone cannot express this).
        private bool _completedRequestWithoutGrant;
        private bool _monitoringStarted;

        public AndroidStepSensorProvider(IClock clock, Log log = null)
        : this(clock, null, null, log)
        {
        }

        public AndroidStepSensorProvider(IClock clock, AndroidJavaObject existingBridge, Log log = null)
        : this(clock, existingBridge, null, log)
        {
        }

        public AndroidStepSensorProvider(IClock clock, AndroidJavaObject existingBridge, AndroidJavaObject existingActivity, Log log = null)
        {
            _log = log ?? Core.Log.Disabled;
            _clock = clock ?? Core.SystemClock.Instance;

            _bridge = existingBridge ?? new AndroidJavaObject("com.walkgame.sensors.StepSensorBridge");
            if (_bridge == null)
            {
                throw new InvalidOperationException("StepSensorBridge not resolvable.");
            }

            if (existingBridge == null || existingActivity == null)
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                _activity = existingActivity ?? player.GetStatic<AndroidJavaObject>("currentActivity");
                _bridge.Call("initialize", _activity);
            }
            else
            {
                _activity = existingActivity;
            }

            // If motion access is already granted (returning user), start listening
            // immediately; otherwise monitoring begins after a successful request.
            if (ReadRefinedPermission() == ActivityPermissionState.Granted)
            {
                EnsureMonitoringStarted();
            }
        }

        public string ProviderId => ProviderIdValue;

        /// <summary>Idempotent listener startup; safe to call repeatedly.</summary>
        public void EnsureMonitoringStarted()
        {
            lock (_gate)
            {
                if (_monitoringStarted || !IsCounterAvailable())
                {
                    return;
                }

                try
                {
                    _monitoringStarted = _bridge.Call<bool>("startMonitoring");
                    if (_monitoringStarted)
                    {
                        _log.Info("Step counter monitoring started.");
                    }
                }
                catch (Exception ex)
                {
                    _log.Warning($"startMonitoring failed: {ex.Message}");
                }
            }
        }

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
                motionPermission = ReadRefinedPermission(),
                locationPermission = ActivityPermissionState.Unavailable,
            };
            return Task.FromResult(capability);
        }

        /// <summary>
        /// Contextual ACTIVITY_RECOGNITION request. Only fires the OS dialog when the
        /// state is effectively NotDetermined; resolves by short-interval polling of
        /// the runtime grant state plus the rationale hint (denials surface quickly),
        /// bounded by a generous timeout for players who leave the dialog open.
        /// </summary>
        public async Task<ActivityPermissionState> RequestMotionPermissionAsync()
        {
            var before = ReadRefinedPermission();
            if (before == ActivityPermissionState.Granted || before == ActivityPermissionState.Unavailable)
            {
                return before;
            }

            try
            {
                _bridge.Call("requestPermission", _activity);
            }
            catch (Exception ex)
            {
                _log.Warning($"requestPermission failed: {ex.Message}");
                return ReadRefinedPermission();
            }

            DateTime deadline = DateTime.UtcNow + RequestPollTimeout;
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(RequestPollIntervalMs);

                var current = ReadRefinedPermission();
                if (current == ActivityPermissionState.Granted)
                {
                    EnsureMonitoringStarted();
                    return current;
                }

                bool rationaleVisible = ReadRationaleHint();
                bool answered = current == ActivityPermissionState.Denied || rationaleVisible;
                if (answered)
                {
                    _completedRequestWithoutGrant = true;
                    return current == ActivityPermissionState.Denied
                        ? current
                        : ActivityPermissionState.Denied;
                }
            }

            // Dialog left open past the timeout: report the honest undecided state.
            return ReadRefinedPermission();
        }

        /// <summary>Drains steps accumulated since the previous successful read.</summary>
        public Task<ActivitySnapshot> ReadSnapshotAsync(ActivityCursor cursor)
        {
            lock (_gate)
            {
                if (!IsCounterAvailable() || ReadRefinedPermission() != ActivityPermissionState.Granted)
                {
                    return Task.FromResult<ActivitySnapshot>(null);
                }

                EnsureMonitoringStarted();
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

                if (ReadRefinedPermission() != ActivityPermissionState.Granted)
                {
                    return Task.FromResult(SessionStartError.PermissionDenied);
                }

                if (_session != null)
                {
                    return Task.FromResult(SessionStartError.AlreadyRunning);
                }

                EnsureMonitoringStarted();
                ConsumeRawCounter();
                long baseline = HasValidBaseline ? (long)_lastRawCounter : 0L;
                _session = new ActiveSessionState
                {
                    sessionType = sessionType,
                    startedAtUtc = _clock.UtcNow,
                    initialStepBaseline = baseline,
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
                double elapsedSeconds = (_clock.UtcNow - _session.startedAtUtc).TotalSeconds;
                return Task.FromResult(new ActiveSessionSample
                {
                    sessionActive = true,
                    accumulatedSteps = CurrentSessionSteps(_session),
                    accumulatedDistanceMeters = _session.accumulatedDistanceMeters,
                    movingSeconds = System.Math.Max(0, elapsedSeconds),
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

            return Task.FromResult(new ActivitySessionResult
            {
                sessionId = finished.sessionId,
                type = finished.sessionType,
                startUtc = finished.startedAtUtc,
                endUtc = _clock.UtcNow,
                acceptedSteps = CurrentSessionSteps(finished),
                verifiedDistanceMeters = 0, // distance requires the optional location flow (Phase 4C)
                verifiedMovingSeconds = System.Math.Max(0, (_clock.UtcNow - finished.startedAtUtc).TotalSeconds),
                cadenceConsistency = null,
            });
        }

        private bool HasValidBaseline => _lastRawCounter > double.MinValue && !double.IsInfinity(_lastRawCounter);

        private long CurrentSessionSteps(ActiveSessionState session)
        {
            if (session == null || !session.HasBaseline || !HasValidBaseline)
            {
                return 0;
            }

            return (long)System.Math.Max(0, _lastRawCounter - session.initialStepBaseline.GetValueOrDefault());
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

        private ActivityPermissionState ReadRefinedPermission()
        {
            int raw;
            try
            {
                raw = _bridge.Call<int>("getAuthorizationStatus");
            }
            catch (Exception ex)
            {
                _log.Warning($"getAuthorizationStatus failed: {ex.Message}");
                return ActivityPermissionState.Unavailable;
            }

            switch (raw)
            {
                case 3: return ActivityPermissionState.Granted;
                case 2: return ActivityPermissionState.Denied;
                case 1:
                    return _completedRequestWithoutGrant || ReadRationaleHint()
                        ? ActivityPermissionState.Denied
                        : ActivityPermissionState.NotDetermined;
                default: return ActivityPermissionState.Unavailable;
            }
        }

        private bool ReadRationaleHint()
        {
            try
            {
                return _activity != null && _bridge.Call<bool>("shouldShowRequestRationale", _activity);
            }
            catch
            {
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
            double raw;
            try
            {
                raw = _bridge.Call<double>("getCumulativeSteps");
            }
            catch (Exception ex)
            {
                _log.Warning($"getCumulativeSteps failed: {ex.Message}");
                return;
            }

            if (double.IsNaN(raw) || double.IsInfinity(raw))
            {
                // No observation yet (bridge initializes to NaN) or a corrupt event:
                // fail closed, keep the previous baseline untouched.
                return;
            }

            if (raw < 0)
            {
                _log.Warning($"Negative cumulative step value {raw}; ignored.");
                return;
            }

            if (!HasValidBaseline)
            {
                // First valid observation after install/process start: establish
                // baseline without crediting anything.
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
