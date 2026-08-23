#if UNITY_IOS && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using WalkGame.Activity;
using WalkGame.Core;

namespace WalkGame.Platform.iOS
{
    /// <summary>
    /// Core Motion adapter over the narrow WG_* bridge (WalkGamePedometerBridge.mm).
    /// Historical reconciliation respects the 7-day CMPedometer window and never
    /// re-credits intervals; reward math stays in C# domain code.
    /// </summary>
    public sealed class IosCoreMotionProvider : IActivityProvider
    {
        public const string ProviderIdValue = "activity.ios.coremotion";
        private static readonly TimeSpan HistoryWindow = TimeSpan.FromDays(7);

        private readonly object _gate = new object();
        private ActiveSessionState _session;
        private DateTime _sessionWallClockStart;
        private double _sessionStartLiveSteps;

        public IosCoreMotionProvider(IClock clock)
        {
            Clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public IClock Clock { get; }

        public string ProviderId => ProviderIdValue;

        public Task<ActivityCapability> GetCapabilityAsync()
        {
            var capability = new ActivityCapability
            {
                supportsPassiveSteps = WG_IsPedometerAvailable() != 0,
                supportsHistoricalQuery = true,
                supportsActiveSession = WG_IsPedometerAvailable() != 0,
                supportsDistance = true,
                supportsCadence = false, // average cadence requires live updates; Phase 4C
                supportsLocationSession = false,
                motionPermission = (ActivityPermissionState)WG_GetAuthorizationStatus(),
                locationPermission = ActivityPermissionState.Unavailable,
            };
            return Task.FromResult(capability);
        }

        public Task<ActivitySnapshot> ReadSnapshotAsync(ActivityCursor cursor)
        {
            if (WG_IsPedometerAvailable() == 0 ||
                (ActivityPermissionState)WG_GetAuthorizationStatus() != ActivityPermissionState.Granted)
            {
                return Task.FromResult<ActivitySnapshot>(null);
            }

            DateTime nowUtc = Clock.UtcNow;
            DateTime since = cursor?.lastSuccessfulSyncUtc ?? nowUtc.AddHours(-24);

            // Respect the seven-day historical window (MOBILE_ACTIVITY_INTEGRATION 3).
            DateTime earliestAvailable = nowUtc - HistoryWindow;
            if (since < earliestAvailable)
            {
                since = earliestAvailable;
            }

            if ((nowUtc - since).TotalSeconds < 60)
            {
                return Task.FromResult<ActivitySnapshot>(null);
            }

            double steps = WG_QueryPedometerSteps(ToUnix(since), ToUnix(nowUtc));
            double distance = WG_QueryPedometerDistance(ToUnix(since), ToUnix(nowUtc));
            if (steps < 0)
            {
                return Task.FromResult<ActivitySnapshot>(null);
            }

            var snapshot = new ActivitySnapshot
            {
                providerId = ProviderId,
                intervalStartUtc = since,
                intervalEndUtc = nowUtc,
                stepCount = (long)steps,
                estimatedDistanceMeters = distance >= 0 ? distance : (double?)null,
                sourceType = ActivitySourceType.PhoneSensor,
                recordingType = ActivityRecordingType.Passive,
                quality = new ActivityQuality
                {
                    hasStepEvidence = steps > 0,
                    hasDistanceEvidence = distance > 0,
                    accuracyScore = 0.7f,
                },
            };
            snapshot.providerRecordIds.Add($"ios.history.{nowUtc.Ticks}");
            return Task.FromResult(snapshot);
        }

        public Task<SessionStartError> StartSessionAsync(SessionType sessionType)
        {
            lock (_gate)
            {
                if (WG_IsPedometerAvailable() == 0)
                {
                    return Task.FromResult(SessionStartError.SensorUnavailable);
                }

                if ((ActivityPermissionState)WG_GetAuthorizationStatus() != ActivityPermissionState.Granted)
                {
                    return Task.FromResult(SessionStartError.PermissionDenied);
                }

                if (_session != null || WG_IsSessionActive() != 0)
                {
                    return Task.FromResult(SessionStartError.AlreadyRunning);
                }

                DateTime start = Clock.UtcNow;
                _sessionWallClockStart = start;
                _session = new ActiveSessionState
                {
                    sessionType = sessionType,
                    startedAtUtc = start,
                    initialStepBaseline = 0,
                };
                WG_StartPedometerUpdates(ToUnix(start));
                _sessionStartLiveSteps = WG_ReadLiveSteps();
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

                double steps = Math.Max(0, WG_ReadLiveSteps() - _sessionStartLiveSteps);
                return Task.FromResult(new ActiveSessionSample
                {
                    sessionActive = true,
                    accumulatedSteps = (long)steps,
                    accumulatedDistanceMeters = WG_ReadLiveDistance(),
                    movingSeconds = (Clock.UtcNow - _sessionWallClockStart).TotalSeconds,
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

            double steps = Math.Max(0, WG_ReadLiveSteps() - _sessionStartLiveSteps);
            double distance = WG_ReadLiveDistance();
            WG_StopPedometerUpdates();

            return Task.FromResult(new ActivitySessionResult
            {
                sessionId = finished.sessionId,
                type = finished.sessionType,
                startUtc = finished.startedAtUtc,
                endUtc = Clock.UtcNow,
                acceptedSteps = (long)steps,
                verifiedDistanceMeters = Math.Max(0, distance),
                verifiedMovingSeconds = (Clock.UtcNow - _sessionWallClockStart).TotalSeconds,
                cadenceConsistency = null,
            });
        }

        private static double ToUnix(DateTime utc)
        {
            return (utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        // Native symbols resolve at link time on device builds (__Internal = static lib).
        [DllImport("__Internal")] private static extern int WG_IsPedometerAvailable();
        [DllImport("__Internal")] private static extern int WG_GetAuthorizationStatus();
        [DllImport("__Internal")] private static extern double WG_QueryPedometerSteps(double startUnix, double endUnix);
        [DllImport("__Internal")] private static extern double WG_QueryPedometerDistance(double startUnix, double endUnix);
        [DllImport("__Internal")] private static extern void WG_StartPedometerUpdates(double startUnix);
        [DllImport("__Internal")] private static extern double WG_ReadLiveSteps();
        [DllImport("__Internal")] private static extern double WG_ReadLiveDistance();
        [DllImport("__Internal")] private static extern int WG_IsSessionActive();
        [DllImport("__Internal")] private static extern void WG_StopPedometerUpdates();
    }
}
#endif
