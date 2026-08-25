using System;
using System.Threading.Tasks;
using WalkGame.Core;

namespace WalkGame.Activity
{
    /// <summary>
    /// Fake provider used before native sensors exist and by the debug menu forever.
    /// Implements every required debug control (ACTIVITY_REWARD_SYSTEM 18):
    /// add steps, simulate walk/run, reboot reset, suspicious vehicle session,
    /// missing cadence/GPS. Game logic cannot tell this apart from a real provider.
    /// </summary>
    public sealed class DebugActivityProvider : IActivityProvider
    {
        public const string ProviderIdValue = "activity.debug";

        private readonly IClock _clock;
        private readonly object _gate = new object();

        private double _rawCumulativeStepCounter = 5000; // pretend device booted a while ago
        private ActivityPermissionState _permission = ActivityPermissionState.Granted;
        private bool _autoGrantOnRequest = true;

        private ActiveSessionState _session;

        // ADR 0009 staging: movement held by an unresolved prepared delivery. The
        // fake counter only truly empties when the application acknowledges a
        // durable commit; rejection returns the staged steps for retry.
        private PreparedActivityDelivery _passiveClaim;
        private string _pendingSessionId;
        private long _pendingSessionSteps;

        public DebugActivityProvider(IClock clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public string ProviderId => ProviderIdValue;

        public bool SimulatedMockLocation { get; set; }
        public bool SimulateMissingCadence { get; set; }
        public bool SimulateSensorUnavailable { get; set; }

        public Task<ActivityCapability> GetCapabilityAsync()
        {
            return Task.FromResult(new ActivityCapability
            {
                supportsPassiveSteps = !SimulateSensorUnavailable,
                supportsHistoricalQuery = true,
                supportsActiveSession = !SimulateSensorUnavailable,
                supportsDistance = true,
                supportsCadence = !SimulateMissingCadence,
                supportsLocationSession = true,
                motionPermission = SimulateSensorUnavailable
                    ? ActivityPermissionState.Unavailable
                    : _permission,
                locationPermission = ActivityPermissionState.NotDetermined,
            });
        }

        /// <summary>Passive delta since cursor: everything accumulated on the fake counter,
        /// staged as a prepared delivery (ADR 0009) instead of being consumed outright.</summary>
        public Task<PreparedActivityDelivery> PreparePassiveDeliveryAsync(ActivityCursor cursor)
        {
            if (SimulateSensorUnavailable || _permission != ActivityPermissionState.Granted)
            {
                return Task.FromResult<PreparedActivityDelivery>(null);
            }

            // While an Expedition runs, the session owns the counter stream (S8).
            long steps;
            lock (_gate)
            {
                if (_session != null || _passiveClaim != null)
                {
                    return Task.FromResult<PreparedActivityDelivery>(null);
                }

                steps = (long)_rawCumulativeStepCounter;
                if (steps <= 0)
                {
                    return Task.FromResult<PreparedActivityDelivery>(null);
                }

                // Stage the movement; it only stays emptied once the delivery is
                // durably acknowledged.
                _rawCumulativeStepCounter = 0;
            }

            var end = _clock.UtcNow;
            var start = cursor?.lastSuccessfulSyncUtc ?? end.AddMinutes(-30);
            var snapshot = new ActivitySnapshot
            {
                providerId = ProviderId,
                intervalStartUtc = start,
                intervalEndUtc = end,
                stepCount = steps,
                estimatedDistanceMeters = null,
                sourceType = ActivitySourceType.PhoneSensor,
                recordingType = ActivityRecordingType.Passive,
                quality = new ActivityQuality
                {
                    hasStepEvidence = true,
                    hasDistanceEvidence = false,
                    hasCadenceEvidence = false,
                    hasLocationEvidence = false,
                    accuracyScore = 0.6f,
                },
            };
            snapshot.providerRecordIds.Add($"debug.passive.{end.Ticks}");
            var delivery = new PreparedActivityDelivery { snapshot = snapshot };

            lock (_gate)
            {
                _passiveClaim = delivery;
            }

            return Task.FromResult(delivery);
        }

        /// <summary>ADR 0009 resolution: acknowledge drops the staged movement; reject
        /// returns it to the fake counter so a retry cannot lose base movement.
        /// Unknown/stale deliveries are ignored (idempotent).</summary>
        public void ResolvePreparedDelivery(PreparedActivityDelivery delivery, bool durable)
        {
            if (delivery == null)
            {
                return;
            }

            lock (_gate)
            {
                if (_passiveClaim == null ||
                    !string.Equals(_passiveClaim.deliveryId, delivery.deliveryId, StringComparison.Ordinal))
                {
                    return;
                }

                long stagedSteps = _passiveClaim.snapshot?.stepCount ?? 0;
                _passiveClaim = null;
                if (!durable)
                {
                    _rawCumulativeStepCounter += stagedSteps;
                }
            }
        }

        /// <summary>ADR 0009 session resolution: reject returns the session's base steps
        /// to the passive counter so they stay recoverable; acknowledge drops them.</summary>
        public void ResolveSessionCompletion(string sessionId, bool durable)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return;
            }

            lock (_gate)
            {
                if (!string.Equals(_pendingSessionId, sessionId, StringComparison.Ordinal))
                {
                    return;
                }

                long steps = _pendingSessionSteps;
                _pendingSessionId = null;
                _pendingSessionSteps = 0;
                if (!durable)
                {
                    _rawCumulativeStepCounter += steps;
                }
            }
        }

        public Task<SessionStartError> StartSessionAsync(SessionType sessionType)
        {
            if (SimulateSensorUnavailable)
            {
                return Task.FromResult(SessionStartError.SensorUnavailable);
            }

            if (_permission != ActivityPermissionState.Granted)
            {
                return Task.FromResult(SessionStartError.PermissionDenied);
            }

            lock (_gate)
            {
                if (_session != null)
                {
                    return Task.FromResult(SessionStartError.AlreadyRunning);
                }

                _session = new ActiveSessionState
                {
                    sessionType = sessionType,
                    startedAtUtc = _clock.UtcNow,
                    initialStepBaseline = (long)_rawCumulativeStepCounter,
                };
            }

            return Task.FromResult(SessionStartError.None);
        }

        /// <summary>Advances the simulated session with plausible pedestrian data.</summary>
        public void SimulateSessionProgress(long additionalSteps, double distanceMeters, double movingSeconds)
        {
            lock (_gate)
            {
                if (_session == null)
                {
                    return;
                }

                _rawCumulativeStepCounter += additionalSteps;
                _session.accumulatedSteps += additionalSteps;
                _session.accumulatedDistanceMeters += distanceMeters;
                _session.movingSeconds += movingSeconds;
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

                return Task.FromResult(new ActiveSessionSample
                {
                    sessionActive = true,
                    accumulatedSteps = _session.accumulatedSteps,
                    accumulatedDistanceMeters = _session.accumulatedDistanceMeters,
                    movingSeconds = _session.movingSeconds,
                    currentCadenceStepsPerMinute = SimulateMissingCadence ? null : 100.0,
                });
            }
        }

        public Task<ActivitySessionResult> StopSessionAsync()
        {
            ActiveSessionState finished;
            lock (_gate)
            {
                finished = _session;
                _session = null;
            }

            if (finished == null)
            {
                return Task.FromResult<ActivitySessionResult>(null);
            }

            var result = new ActivitySessionResult
            {
                sessionId = finished.sessionId,
                type = finished.sessionType,
                startUtc = finished.startedAtUtc,
                endUtc = _clock.UtcNow,
                acceptedSteps = finished.accumulatedSteps,
                verifiedDistanceMeters = finished.accumulatedDistanceMeters,
                verifiedMovingSeconds = finished.movingSeconds,
                cadenceConsistency = SimulateMissingCadence ? null : 0.9f,
            };

            // Session-owned progress leaves the passive stream here: it is held as
            // the completion claim until the profile commit resolves. Without this
            // subtraction the same steps would be delivered passively after the
            // session even when its result committed durably (M8.3 audit fix).
            lock (_gate)
            {
                _rawCumulativeStepCounter = Math.Max(0, _rawCumulativeStepCounter - finished.accumulatedSteps);
                _pendingSessionId = finished.sessionId;
                _pendingSessionSteps = finished.accumulatedSteps;
            }

            return Task.FromResult(result);
        }

        /// <summary>Debug action: +1,000 steps into the passive counter.</summary>
        public void DebugAddSteps(long steps)
        {
            lock (_gate)
            {
                _rawCumulativeStepCounter += Math.Max(0, steps);
            }
        }

        /// <summary>Debug action: simulate device reboot (counter resets to zero).</summary>
        public void DebugSimulateReboot()
        {
            lock (_gate)
            {
                _rawCumulativeStepCounter = 0;
            }
        }

        /// <summary>Debug action: vehicle-like active session used to verify bonus rejection.</summary>
        public SessionStartError DebugBeginVehicleLikeSession(out VehicleSessionDriver driver)
        {
            driver = null;
            var error = StartSessionAsync(SessionType.Walk).GetAwaiter().GetResult();
            if (error != SessionStartError.None)
            {
                return error;
            }

            driver = new VehicleSessionDriver(this);
            return SessionStartError.None;
        }

        public void DebugSetPermission(bool granted)
        {
            SetSimulatedPermission(granted ? ActivityPermissionState.Granted : ActivityPermissionState.Denied);
        }

        /// <summary>Debug/test control: full permission state including NotDetermined.</summary>
        public void SetSimulatedPermission(ActivityPermissionState state)
        {
            _permission = state;
        }

        /// <summary>Debug/test control: when false, requests stay NotDetermined so callers
        /// can exercise the still-undecided path deterministically.</summary>
        public void SetAutoGrantOnRequest(bool autoGrant)
        {
            _autoGrantOnRequest = autoGrant;
        }

        /// <summary>
        /// Mirrors platform semantics: only NotDetermined triggers a "prompt"; denial is
        /// sticky until something external changes it (Settings toggle / test code).
        /// </summary>
        public Task<ActivityPermissionState> RequestMotionPermissionAsync()
        {
            if (_permission == ActivityPermissionState.NotDetermined && _autoGrantOnRequest)
            {
                _permission = ActivityPermissionState.Granted;
            }

            return Task.FromResult(_permission);
        }

        /// <summary>
        /// Debug action: drives implausible vehicle-like data into the CURRENT session.
        /// Must be called between StartSessionAsync and StopSessionAsync; coroutine-safe
        /// (no blocking) so UI flows never need .Result on the provider surface.
        /// </summary>
        public void SimulateVehicleDrive(double minutes, double speedKmh = 60)
        {
            double hours = minutes / 60.0;
            double meters = speedKmh * 1000.0 * hours;
            // A car produces almost no pedestrian steps relative to distance.
            long fakeSteps = (long)(minutes * 2);
            SimulateSessionProgress(fakeSteps, meters, minutes * 60.0);
        }

        /// <summary>Drives implausible high-speed movement into the current session.</summary>
        public sealed class VehicleSessionDriver
        {
            private readonly DebugActivityProvider _owner;

            internal VehicleSessionDriver(DebugActivityProvider owner)
            {
                _owner = owner;
            }

            /// <summary>60 km/h for N minutes: distance huge, step cadence near zero.</summary>
            public void Drive(double minutes, double speedKmh = 60)
            {
                _owner.SimulateVehicleDrive(minutes, speedKmh);
            }
        }
    }
}
