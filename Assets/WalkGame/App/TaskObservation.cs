using System;
using System.Threading;
using System.Threading.Tasks;

namespace WalkGame.App
{
    /// <summary>
    /// Bridges a Task into Unity's coroutine lifecycle without synchronously reading
    /// Task.Result. The observer captures completion state even when the native
    /// provider completes on a worker thread, and it always observes exceptions.
    /// </summary>
    internal sealed class TaskObservation<T>
    {
        private int _completed;

        // Completion is written by an awaited continuation and read by Unity's
        // coroutine on the main thread. Volatile publication makes the captured
        // value/error visible before the coroutine consumes the observation.
        public bool IsCompleted => Volatile.Read(ref _completed) != 0;
        public bool IsFaulted { get; internal set; }
        public bool IsCanceled { get; internal set; }
        public Exception Exception { get; internal set; }
        public T Value { get; internal set; }

        internal void MarkCompleted()
        {
            Volatile.Write(ref _completed, 1);
        }
    }

    internal static class TaskObservation
    {
        public static async Task Observe<T>(Task<T> task, TaskObservation<T> observation)
        {
            try
            {
                observation.Value = await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException ex)
            {
                observation.IsCanceled = true;
                observation.Exception = ex;
            }
            catch (Exception ex)
            {
                observation.IsFaulted = true;
                observation.Exception = ex;
            }
            finally
            {
                observation.MarkCompleted();
            }
        }
    }
}
