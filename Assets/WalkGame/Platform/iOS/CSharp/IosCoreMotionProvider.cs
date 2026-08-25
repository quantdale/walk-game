#if UNITY_IOS && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using AOT;
using UnityEngine;
using WalkGame.Activity;
using WalkGame.Core;

namespace WalkGame.Platform.iOS
{
    /// <summary>
    /// Core Motion adapter over the narrow WG_* bridge (WalkGamePedometerBridge.mm).
    /// Historical reconciliation is fully asynchronous: the native side delivers a
    /// combined steps+distance result through a marshalled callback, so Unity's main
    /// thread never blocks on a semaphore (campaign S6/S7). Window planning, cursor
    /// semantics and reward math stay in engine-free C#.
    /// </summary>
    public sealed class IosCoreMotionProvider : IActivityProvider
    {
        public const string ProviderIdValue = "activity.ios.coremotion";

        private static readonly TimeSpan RequestPollTimeout = TimeSpan.FromSeconds(120);
        private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(10);

        // Marshalled once per process; results arrive on a native serial queue, never
        // on Unity's main thread, and only touch this dictionary + task sources.
        private static readonly object PendingGate = new object();
        private static readonly Dictionary<int, TaskCompletionSource<IosQueryOutcome>> PendingQueries =
            new Dictionary<int, TaskCompletionSource<IosQueryOutcome>>();
        private static bool _callbackRegistered;
        private static int _lastIssuedRequestId;

        private readonly object _gate = new object();
        private readonly IosHistoryWindowPlanner _planner = new IosHistoryWindowPlanner();
        private ActiveSessionState _session;
        private double _sessionStartLiveSteps;

        public IosCoreMotionProvider(IClock clock)
        {
            Clock = clock ?? throw new ArgumentNullException(nameof(clock));
            RegisterCallbackOnce();
        }

        public IClock Clock { get; }

        public string ProviderId => ProviderIdValue;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void QueryResultCallback(int requestId, double steps, double distance, int errorCode);

        [DllImport("__Internal")]
        private static extern void WG_SetQueryResultCallback(QueryResultCallback callback);

        [DllImport("__Internal")]
        private static extern int WG_QueryPedometerAsync(double startUnix, double endUnix);

        [DllImport("__Internal")] private static extern int WG_IsPedometerAvailable();
        [DllImport("__Internal")] private static extern int WG_GetAuthorizationStatus();
        [DllImport("__Internal")] private static extern void WG_StartPedometerUpdates(double startUnix);
        [DllImport("__Internal")] private static extern double WG_ReadLiveSteps();
        [DllImport("__Internal")] private static extern double WG_ReadLiveDistance();
        [DllImport("__Internal")] private static extern int WG_IsSessionActive();
        [DllImport("__Internal")] private static extern void WG_StopPedometerUpdates();

        private static void RegisterCallbackOnce()
        {
            if (_callbackRegistered)
            {
                return;
            }

            _callbackRegistered = true;
            WG_SetQueryResultCallback(OnQueryResult);
        }

        [MonoPInvokeCallback(typeof(QueryResultCallback))]
        private static void OnQueryResult(int requestId, double steps, double distance, int errorCode)
        {
            TaskCompletionSource<IosQueryOutcome> source;
            lock (PendingGate)
            {
                if (!PendingQueries.TryGetValue(requestId, out source))
                {
                    return; // stale/late answer for an already-timed-out request: drop it
                }

                PendingQueries.Remove(requestId);
            }

            source.TrySetResult(new IosQueryOutcome(steps, distance, errorCode));
        }

        /// <summary>Issues one async query; null outcome means start failure or timeout.</summary>
        private static async Task<IosQueryOutcome?> QueryAsync(double startUnix, double endUnix)
        {
            int requestId;
            var source = new TaskCompletionSource<IosQueryOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);

            lock (PendingGate)
            {
                requestId = WG_QueryPedometerAsync(startUnix, endUnix);
                if (requestId <= 0)
                {
                    return null;
                }

                _lastIssuedRequestId = requestId;
                PendingQueries[requestId] = source;
            }

            var completed = await Task.WhenAny(source.Task, Task.Delay(QueryTimeout));
            if (completed != source.Task)
            {
                lock (PendingGate)
                {
                    PendingQueries.Remove(requestId); // late native answers are dropped as stale
                }

                return null;
            }

            return await source.Task;
        }

        private readonly struct IosQueryOutcome
        {
            public IosQueryOutcome(double steps, double distance, int errorCode)
            {
                Steps = steps;
                DistanceMeters = distance;
                ErrorCode = errorCode;
            }

            public double Steps { get; }
            public double DistanceMeters { get; }
            public int ErrorCode { get; }
            public bool Failed => ErrorCode != 0 || Steps < 0;
        }

        /// <summary>
        /// Contextual Motion &amp; Fitness request. iOS only surfaces its prompt when an
        /// app first touches a Core Motion API while authorization is NotDetermined, so
        /// a benign asynchronous one-minute query is used as the trigger and the status
        /// is polled until the user answers (or the generous timeout lapses). Calling
        /// this when already decided never re-prompts - matching platform semantics.
        /// </summary>
        public async Task<ActivityPermissionState> RequestMotionPermissionAsync()
        {
            var before = (ActivityPermissionState)WG_GetAuthorizationStatus();
            if (before != ActivityPermissionState.NotDetermined)
            {
                return before;
            }

            DateTime nowUtc = Clock.UtcNow;
            // Result deliberately ignored: issuing the query is what triggers the dialog.
            await QueryAsync(ToUnix(nowUtc.AddMinutes(-1)), ToUnix(nowUtc));

            DateTime deadline = Clock.UtcNow + RequestPollTimeout;
            while (Clock.UtcNow < deadline)
            {
                await Task.Delay(300);
                var current = (ActivityPermissionState)WG_GetAuthorizationStatus();
                if (current != ActivityPermissionState.NotDetermined)
                {
                    return current;
                }
            }

            return ActivityPermissionState.NotDetermined;
        }

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

        /// <summary>
        /// Prepares the next passive delivery from a historical query. Core Motion's
        /// absolute history makes this naturally retryable (ADR 0009): nothing private
        /// is consumed at preparation, and the durable successful-sync cursor only
        /// advances inside the committed profile - so a failed application commit
        /// leaves this exact time window queryable again.
        /// </summary>
        public async Task<PreparedActivityDelivery> PreparePassiveDeliveryAsync(ActivityCursor cursor)
        {
            if (WG_IsPedometerAvailable() == 0 ||
                (ActivityPermissionState)WG_GetAuthorizationStatus() != ActivityPermissionState.Granted)
            {
                return null;
            }

            // An active Expedition owns its movement window; passive historical queries
            // overlapping it would double-credit the same steps (campaign S8).
            lock (_gate)
            {
                if (_session != null || WG_IsSessionActive() != 0)
                {
                    return null;
                }
            }

            DateTime nowUtc = Clock.UtcNow;
            if (!_planner.TryPlan(cursor?.lastSuccessfulSyncUtc, nowUtc, out var since, out var until))
            {
                return null;
            }

            IosQueryOutcome? pending = await QueryAsync(ToUnix(since), ToUnix(until));
            if (!pending.HasValue || pending.Value.Failed)
            {
                // Failed sensor queries fail closed: no snapshot, durable cursor stays
                // where it was so the same window is retried next cycle.
                return null;
            }

            double steps = Math.Max(0, pending.Value.Steps);
            double distance = pending.Value.DistanceMeters >= 0 ? pending.Value.DistanceMeters : 0;

            var snapshot = new ActivitySnapshot
            {
                providerId = ProviderId,
                intervalStartUtc = since,
                intervalEndUtc = until,
                stepCount = (long)steps,
                estimatedDistanceMeters = distance > 0 ? distance : (double?)null,
                sourceType = ActivitySourceType.PhoneSensor,
                recordingType = ActivityRecordingType.Passive,
                quality = new ActivityQuality
                {
                    hasStepEvidence = steps > 0,
                    hasDistanceEvidence = distance > 0,
                    accuracyScore = 0.7f,
                },
            };
            snapshot.providerRecordIds.Add($"ios.history.{until.Ticks}");
            return new PreparedActivityDelivery { snapshot = snapshot };
        }

        /// <summary>ADR 0009 resolution: no provider-private movement was consumed by
        /// preparation, so there is nothing to restore or drop here. A rejected commit
        /// rewinds the durable sync cursor with the profile rollback, making the same
        /// historical window retryable; duplicate intervals stay suppressed by durable
        /// dedup/cursor state once anything does commit.</summary>
        public void ResolvePreparedDelivery(PreparedActivityDelivery delivery, bool durable)
        {
        }

        /// <summary>ADR 0009 session resolution: live caches reset natively at stop and
        /// the completed window remains recoverable through the historical query path
        /// because the rolled-back profile never advanced lastSuccessfulSyncUtc past it.</summary>
        public void ResolveSessionCompletion(string sessionId, bool durable)
        {
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

                double elapsedSeconds = Math.Max(0, (Clock.UtcNow - _session.startedAtUtc).TotalSeconds);
                return Task.FromResult(new ActiveSessionSample
                {
                    sessionActive = true,
                    accumulatedSteps = CurrentLiveSteps(),
                    accumulatedDistanceMeters = Math.Max(0, WG_ReadLiveDistance()),
                    movingSeconds = elapsedSeconds,
                });
            }
        }

        public Task<ActivitySessionResult> StopSessionAsync()
        {
            ActiveSessionState finished;
            double steps;
            double distance;

            lock (_gate)
            {
                finished = _session;
                if (finished == null)
                {
                    return Task.FromResult<ActivitySessionResult>(null);
                }

                _session = null;
                steps = CurrentLiveSteps();
                distance = Math.Max(0, WG_ReadLiveDistance());
                WG_StopPedometerUpdates(); // live updates stop cleanly; caches reset natively
            }

            double movingSeconds = Math.Max(0, (Clock.UtcNow - finished.startedAtUtc).TotalSeconds);
            return Task.FromResult(new ActivitySessionResult
            {
                sessionId = finished.sessionId,
                type = finished.sessionType,
                startUtc = finished.startedAtUtc,
                endUtc = Clock.UtcNow,
                acceptedSteps = (long)steps,
                verifiedDistanceMeters = distance,
                verifiedMovingSeconds = movingSeconds,
                cadenceConsistency = null,
            });
        }

        private double CurrentLiveSteps()
        {
            return Math.Max(0, WG_ReadLiveSteps() - _sessionStartLiveSteps);
        }

        private static double ToUnix(DateTime utc)
        {
            return (utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }
    }
}
#endif
