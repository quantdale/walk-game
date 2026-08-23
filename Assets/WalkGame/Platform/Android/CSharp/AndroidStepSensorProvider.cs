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
    ///  - Cumulative-counter folding (NaN guards, reboot re-baselining, plausibility
    ///    caps, persisted cursors) lives in AndroidCounterReconciler so every rule
    ///    is domain-testable without hardware.
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
        private readonly AndroidCounterReconciler _reconciler = new AndroidCounterReconciler();
        private readonly ActivitySyncState _syncState;
        private ActiveSessionState _session;

        // Runtime refinement over the native tri-state: once a request round has
        // completed without grant, later non-granted reports mean "denied", not
        // "never asked" (Android 11+ stops showing the dialog entirely after
        // repeated denials, so the raw status alone cannot express this).
        private bool _completedRequestWithoutGrant;
        private bool _monitoringStarted;
        private long _pendingAtSessionStart;

        public AndroidStepSensorProvider(IClock clock, Log log = null)
        : this(clock, null, null, null, log)
        {
        }

        /// <summary>Production constructor: seeds counter reconciliation from the
        /// persisted activity cursor so process restarts do not drop steps.</summary>
        public AndroidStepSensorProvider(IClock clock, ActivitySyncState syncState, Log log = null)
        : this(clock, null, null, syncState, log)
        {
        }

        public AndroidStepSensorProvider(IClock clock, AndroidJavaObject existingBridge, Log log = null)
        : this(clock, existingBridge, null, null, log)
        {
        }

        public AndroidStepSensorProvider(
            IClock clock,
            AndroidJavaObject existingBridge,
            AndroidJavaObject existingActivity,
            ActivitySyncState syncState,
            Log log = null)
        {
            _log = log ?? Core.Log.Disabled;
            _clock = clock ?? Core.SystemClock.Instance;
            _syncState = syncState;

            if (_syncState != null)
            {
                // Resume from the persisted counter cursor: steps accumulated between
                // the last save and this process start are credited instead of lost;
                // a lower live value afterwards is handled as reboot by Fold().
                _reconciler.SeedFromPersistedCounter(_syncState.androidLastRawStepCounter);
            }

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
                    _log.Warning($"startMonitoring failed ({ex.GetType().Name}).");
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
                _log.Warning($"requestPermission failed ({ex.GetType().Name}).");
                return ReadRefinedPermission();
            }

            DateTime deadline = _clock.UtcNow + RequestPollTimeout;
            while (_clock.UtcNow < deadline)
            {
                await Task.Delay(RequestPollIntervalMs);

                var current = ReadRefinedPermission();
                if (current == ActivityPermissionState.Granted)
                {
                    EnsureMonitoringStarted();
                    return current;
                }

                bool rationaleVisible = ReadRationaleHint();
                if (current == ActivityPermissionState.Denied || rationaleVisible)
                {
                    _completedRequestWithoutGrant = true;
                    return ActivityPermissionState.Denied;
                }
            }

            // Dialog left open past the timeout: report the honest undecided state.
            return ReadRefinedPermission();
        }

        /// <summary>Drains steps accumulated since the previous successful read. While an
        /// Expedition session is active it owns the whole counter stream, so passive
        /// reads are suppressed and cannot double-credit its steps (campaign S8).</summary>
        public Task<ActivitySnapshot> ReadSnapshotAsync(ActivityCursor cursor)
        {
            lock (_gate)
            {
                if (!IsCounterAvailable() || ReadRefinedPermission() != ActivityPermissionState.Granted)
                {
                    return Task.FromResult<ActivitySnapshot>(null);
                }

                if (_session != null)
                {
                    return Task.FromResult<ActivitySnapshot>(null);
                }

                EnsureMonitoringStarted();
                ConsumeRawCounter();

                long pending = _reconciler.DrainPending();
                if (pending <= 0)
                {
                    return Task.FromResult<ActivitySnapshot>(null);
                }

                DateTime end = _clock.UtcNow;
                PersistCursor(end);

                var snapshot = new ActivitySnapshot
                {
                    providerId = ProviderId,
                    intervalStartUtc = cursor?.lastSuccessfulSyncUtc ?? end.AddMinutes(-30),
                    intervalEndUtc = end,
                    stepCount = pending,
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
                _session = new ActiveSessionState
                {
                    sessionType = sessionType,
                    startedAtUtc = _clock.UtcNow,
                    initialStepBaseline = _reconciler.HasBaseline ? (long)_reconciler.LastRawCounter.GetValueOrDefault() : 0L,
                };
                // Everything already folded but not yet drained belongs to the passive
                // timeline BEFORE this session; remembered so completion can restore it.
                _pendingAtSessionStart = _reconciler.PendingDelta;
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

            long sessionSteps = CurrentSessionSteps(finished);

            // Partition the folded stream: deltas folded during the session belong to
            // the Expedition; anything from before its start returns to passive credit.
            long drainedNow = _reconciler.DrainPending();
            long preSessionResidue = Math.Min(_pendingAtSessionStart, drainedNow);
            _reconciler.RestorePending(preSessionResidue);
            _pendingAtSessionStart = 0;

            DateTime endUtc = _clock.UtcNow;
            PersistCursor(endUtc);
            return Task.FromResult(new ActivitySessionResult
            {
                sessionId = finished.sessionId,
                type = finished.sessionType,
                startUtc = finished.startedAtUtc,
                endUtc = endUtc,
                acceptedSteps = sessionSteps,
                verifiedDistanceMeters = 0, // distance requires the optional location flow (Phase 4C)
                verifiedMovingSeconds = System.Math.Max(0, (endUtc - finished.startedAtUtc).TotalSeconds),
                cadenceConsistency = null,
            });
        }

        private long CurrentSessionSteps(ActiveSessionState session)
        {
            if (session == null || !session.HasBaseline || !_reconciler.HasBaseline)
            {
                return 0;
            }

            double current = _reconciler.LastRawCounter.GetValueOrDefault();
            return (long)System.Math.Max(0, current - session.initialStepBaseline.GetValueOrDefault());
        }

        private void PersistCursor(DateTime observedUtc)
        {
            if (_syncState == null)
            {
                return;
            }

            _syncState.androidLastRawStepCounter = _reconciler.LastRawCounter;
            _syncState.androidLastCounterObservedUtc = observedUtc;
        }

        private bool IsCounterAvailable()
        {
            try
            {
                return _bridge != null && _bridge.Call<bool>("isStepCounterAvailable");
            }
            catch (Exception ex)
            {
                _log.Warning($"Step counter availability check failed ({ex.GetType().Name}).");
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
                _log.Warning($"getAuthorizationStatus failed ({ex.GetType().Name}).");
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

        /// <summary>Reads one absolute raw sample through the folding state machine.</summary>
        private void ConsumeRawCounter()
        {
            double raw;
            try
            {
                raw = _bridge.Call<double>("getCumulativeSteps");
            }
            catch (Exception ex)
            {
                _log.Warning($"getCumulativeSteps failed ({ex.GetType().Name}).");
                return;
            }

            switch (_reconciler.Fold(raw))
            {
                case CounterFoldOutcome.InvalidSample:
                    // No observation yet (bridge initializes to NaN) or corrupt event:
                    // fail closed, previous baseline untouched.
                    break;
                case CounterFoldOutcome.Rebaselined:
                    _log.Info("Step counter decreased; treating as reboot and re-baselining.");
                    break;
                case CounterFoldOutcome.AnomalyRebaselined:
                    _log.Warning("Implausible step-counter jump; re-baselined without crediting.");
                    break;
            }
        }
    }
}
#endif
