using System;
using System.Threading;
using System.Threading.Tasks;
using WalkGame.Core;

namespace WalkGame.Activity
{
    /// <summary>
    /// Engine-free terminal-ownership token for one asynchronous provider operation
    /// (M8.5 runtime-ownership, ADR 0011). Exactly one terminal transition can ever
    /// succeed: the owning coroutine adopts completion (<see cref="TryAdopt"/>), or a
    /// timeout/cancellation/teardown path abandons it (<see cref="TryAbandon"/>) and
    /// thereby transfers ownership to the deterministic cleanup continuation registered
    /// by <see cref="ProviderOperations"/>. Never both owners, never neither - which is
    /// what makes timeout/completion races certifiable without a Unity loop.
    /// </summary>
    public sealed class OperationLease
    {
        private const int StateOpen = 0;
        private const int StateAdopted = 1;
        private const int StateAbandoned = 2;

        private int _state = StateOpen;

        public bool IsOpen => Volatile.Read(ref _state) == StateOpen;
        public bool IsAdopted => Volatile.Read(ref _state) == StateAdopted;
        public bool IsAbandoned => Volatile.Read(ref _state) == StateAbandoned;

        /// <summary>Claims completion processing exactly once.</summary>
        public bool TryAdopt()
        {
            return Interlocked.CompareExchange(ref _state, StateAdopted, StateOpen) == StateOpen;
        }

        /// <summary>Transfers terminal ownership to the cleanup owner exactly once.</summary>
        public bool TryAbandon()
        {
            return Interlocked.CompareExchange(ref _state, StateAbandoned, StateOpen) == StateOpen;
        }
    }

    /// <summary>
    /// Deterministic late-result cleanup owners for abandoned provider operations
    /// (M8.5 design section 5). After a scheduling timeout or owner teardown the Unity
    /// coroutine may stop observing an operation, but the underlying task can still
    /// complete later - possibly holding provider-private claim state. These helpers
    /// register the cleanup owner BEFORE the observer exits, so every possible future
    /// completion has exactly one owner that converges provider state:
    ///
    ///  - abandoned passive preparations are REJECTED unprocessed (durable=false), so
    ///    staged movement returns to retryable pending and cursors stay untouched;
    ///  - abandoned active-session stops resolve their result non-durably, returning
    ///    the session's base movement to the passive stream exactly once;
    ///  - observational operations (poll/capability/permission) drop late results.
    ///
    /// Continuations touch only thread-safe provider instances - never canonical
    /// profile state, never destroyed Unity objects - so an old generation completing
    /// after recomposition cannot mutate the new runtime (invariant I2/I4).
    /// Cancellation is never treated as durable acknowledgment (invariant I3).
    /// </summary>
    public static class ProviderOperations
    {
        /// <summary>
        /// Abandons a pending passive preparation and installs its cleanup owner.
        /// Returns false when completion already won the race (caller then processes
        /// the observed value normally); returns true when this call became the sole
        /// terminal owner and any late delivery will be rejected automatically.
        /// </summary>
        public static bool AbandonPreparation(
            Task<PreparedActivityDelivery> task,
            OperationLease lease,
            IActivityProvider provider,
            Action<Exception> onFault = null)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));
            if (lease == null) throw new ArgumentNullException(nameof(lease));
            if (provider == null) throw new ArgumentNullException(nameof(provider));

            if (!lease.TryAbandon())
            {
                return false;
            }

            task.ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    onFault?.Invoke(t.Exception?.GetBaseException());
                    return;
                }

                if (t.IsCanceled || t.Result?.snapshot == null)
                {
                    return; // nothing was staged; provider state already convergent
                }

                // Reject WITHOUT processing: movement returns to retryable pending,
                // sync cursor untouched; the next reconcile re-delivers it once.
                ActivityTransactionCoordinator.RejectAbandonedPreparation(provider, t.Result);
            }, TaskContinuationOptions.ExecuteSynchronously);

            return true;
        }

        /// <summary>
        /// Abandons a pending session stop and installs its cleanup owner. A late
        /// result resolves NON-durably (no reward was processed for it), returning the
        /// held base movement to the passive stream exactly once.
        /// </summary>
        public static bool AbandonSessionStop(
            Task<ActivitySessionResult> task,
            OperationLease lease,
            IActivityProvider provider,
            Action<Exception> onFault = null)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));
            if (lease == null) throw new ArgumentNullException(nameof(lease));
            if (provider == null) throw new ArgumentNullException(nameof(provider));

            if (!lease.TryAbandon())
            {
                return false;
            }

            task.ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    onFault?.Invoke(t.Exception?.GetBaseException());
                    return;
                }

                var result = t.Result;
                if (t.IsCanceled || string.IsNullOrEmpty(result?.sessionId))
                {
                    return; // no usable result and nothing held by the provider
                }

                provider.ResolveSessionCompletion(result.sessionId, durable: false);
            }, TaskContinuationOptions.ExecuteSynchronously);

            return true;
        }

        /// <summary>
        /// Abandons an observational operation whose late result must simply be
        /// dropped (poll samples, capability/permission probes). Faults are still
        /// observed so they never surface as unobserved task exceptions.
        /// </summary>
        public static bool DiscardLateResult<T>(
            Task<T> task,
            OperationLease lease,
            Action<Exception> onFault = null)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));
            if (lease == null) throw new ArgumentNullException(nameof(lease));

            if (!lease.TryAbandon())
            {
                return false;
            }

            task.ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    onFault?.Invoke(t.Exception?.GetBaseException());
                }
            }, TaskContinuationOptions.ExecuteSynchronously);

            return true;
        }
    }

    /// <summary>
    /// Aborts a provider session that STARTED but was never adopted by the domain
    /// (M8.5 start-adoption rule): the session is stopped and its held base movement
    /// is returned to the passive stream non-durably, so nothing leaks and nothing is
    /// credited. Fire-and-forget with a single terminal owner (the abort continuation);
    /// touches only the provider instance, making it safe even if the requesting
    /// controller is destroyed first.
    /// </summary>
    public static class ActiveSessionAbort
    {
        public static void Abort(IActivityProvider provider, Action<Exception> onFault = null)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));

            provider.StopSessionAsync().ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    onFault?.Invoke(t.Exception?.GetBaseException());
                    return;
                }

                var result = t.Result;
                if (t.IsCanceled || string.IsNullOrEmpty(result?.sessionId))
                {
                    return;
                }

                provider.ResolveSessionCompletion(result.sessionId, durable: false);
            }, TaskContinuationOptions.ExecuteSynchronously);
        }
    }
}
