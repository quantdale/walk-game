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
        private bool _permissionGranted = true;

        private ActiveSessionState _session;

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
                    : _permissionGranted ? ActivityPermissionState.Granted : ActivityPermissionState.Denied,
                locationPermission = ActivityPermissionState.NotDetermined,
            });
        }

        /// <summary>Passive delta since cursor: everything accumulated on the fake counter.</summary>
        public Task<ActivitySnapshot> ReadSnapshotAsync(ActivityCursor cursor)
        {
            if (SimulateSensorUnavailable || !_permissionGranted)
            {
                return Task.FromResult<ActivitySnapshot>(null);
            }

            long steps;
            lock (_gate)
            {
                steps = (long)_rawCumulativeStepCounter;
                // Passive reads consume the counter like an OS reconciliation would.
                _rawCumulativeStepCounter = 0;
            }

            if (steps <= 0)
            {
                return Task.FromResult<ActivitySnapshot>(null);
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
            return Task.FromResult(snapshot);
        }

        public Task<SessionStartError> StartSessionAsync(SessionType sessionType)
        {
            if (SimulateSensorUnavailable)
            {
                return Task.FromResult(SessionStartError.SensorUnavailable);
            }

            if (!_permissionGranted)
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
            _permissionGranted = granted;
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
                double hours = minutes / 60.0;
                double meters = speedKmh * 1000.0 * hours;
                // A car produces almost no pedestrian steps relative to distance.
                long fakeSteps = (long)(minutes * 2);
                _owner.SimulateSessionProgress(fakeSteps, meters, minutes * 60.0);
            }
        }
    }
}
