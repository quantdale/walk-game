using System;

namespace WalkGame.Core
{
    /// <summary>
    /// Real-time clock with an adjustable offset. The debug "advance clock" action moves
    /// this offset, letting developers exercise offline production windows without
    /// touching device settings. Offset only moves forward through the debug API;
    /// backward jumps are clamped so tests stay deterministic.
    /// </summary>
    public sealed class OffsetClock : IClock
    {
        private readonly IClock _source;
        private TimeSpan _offset;

        public OffsetClock(IClock source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        public DateTime UtcNow => _source.UtcNow + _offset;

        public void Advance(TimeSpan delta)
        {
            if (delta < TimeSpan.Zero)
            {
                delta = TimeSpan.Zero;
            }

            _offset += delta;
        }

        public void Reset()
        {
            _offset = TimeSpan.Zero;
        }
    }
}
