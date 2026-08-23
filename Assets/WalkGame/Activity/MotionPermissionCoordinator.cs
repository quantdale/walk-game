using System;
using System.Threading.Tasks;
using WalkGame.Core;

namespace WalkGame.Activity
{
    public enum MotionPermissionOutcome
    {
        Granted = 0,
        Denied = 1,
        StillNotDetermined = 2,
        Unavailable = 3
    }

    /// <summary>
    /// Runtime permission state machine for movement access (MOBILE_ACTIVITY_INTEGRATION 15/16,
    /// PRIVACY_SAFETY_ANTI_CHEAT 4). The platform is the only authority for the underlying
    /// state; this coordinator sequences contextual requests, exposes change notifications
    /// for UI, and guarantees denial is a normal, non-breaking state. Engine-free so the
    /// transitions are domain-testable against any IActivityProvider fake.
    /// </summary>
    public sealed class MotionPermissionCoordinator
    {
        private readonly IActivityProvider _provider;
        private readonly Log _log;

        public MotionPermissionCoordinator(IActivityProvider provider, Log log = null)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _log = log ?? Core.Log.Disabled;
        }

        /// <summary>Last state observed from the platform. Defaults to NotDetermined,
        /// which is the correct assumption on a fresh install.</summary>
        public ActivityPermissionState CurrentState { get; private set; } = ActivityPermissionState.NotDetermined;

        /// <summary>True while a request/retry round-trip is in flight; UI must not stack prompts.</summary>
        public bool RequestInFlight { get; private set; }

        public event Action<ActivityPermissionState> StateChanged;

        /// <summary>Re-reads the platform state without prompting (e.g. after returning
        /// from system settings). Returns the refreshed state.</summary>
        public async Task<ActivityPermissionState> RefreshAsync()
        {
            var capability = await _provider.GetCapabilityAsync();
            SetState(capability?.motionPermission ?? ActivityPermissionState.Unavailable);
            return CurrentState;
        }

        /// <summary>
        /// Runs one contextual request round-trip after explicit user intent.
        /// Unavailable providers short-circuit without touching the OS; denial maps to a
        /// normal outcome, never an exception path.
        /// </summary>
        public async Task<MotionPermissionOutcome> RequestAsync()
        {
            if (RequestInFlight)
            {
                return MotionPermissionOutcome.StillNotDetermined;
            }

            if (CurrentState == ActivityPermissionState.Unavailable)
            {
                return MotionPermissionOutcome.Unavailable;
            }

            RequestInFlight = true;
            try
            {
                var after = await _provider.RequestMotionPermissionAsync();
                SetState(after);
                switch (CurrentState)
                {
                    case ActivityPermissionState.Granted: return MotionPermissionOutcome.Granted;
                    case ActivityPermissionState.Denied: return MotionPermissionOutcome.Denied;
                    default: return MotionPermissionOutcome.StillNotDetermined;
                }
            }
            catch (Exception ex)
            {
                // A failing permission probe must never break gameplay; treat as unknown
                // and let the next RefreshAsync reconcile with the platform.
                _log.Warning($"Motion permission request failed: {ex.Message}");
                return MotionPermissionOutcome.StillNotDetermined;
            }
            finally
            {
                RequestInFlight = false;
            }
        }

        private void SetState(ActivityPermissionState state)
        {
            if (CurrentState == state)
            {
                return;
            }

            CurrentState = state;
            _log.Info($"Motion permission state: {state}.");
            StateChanged?.Invoke(state);
        }
    }
}
