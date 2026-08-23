using System;

namespace WalkGame.Core
{
    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
        Off = 4
    }

    /// <summary>
    /// Logging seam. Sensitive data (raw sensor values, GPS traces) must never be
    /// logged at Info or above; release builds can raise the minimum level.
    /// Timestamp prefixes intentionally use wall clock: diagnostics only, never
    /// economic or lifecycle state (campaign S9).
    /// </summary>
    public interface ILog
    {
        void Log(LogLevel level, string message);
    }

    public sealed class Log
    {
        private readonly ILog _sink;
        private readonly LogLevel _minimumLevel;

        public Log(ILog sink, LogLevel minimumLevel)
        {
            _sink = sink ?? NullLog.Instance;
            _minimumLevel = minimumLevel;
        }

        public static Log Disabled { get; } = new Log(NullLog.Instance, LogLevel.Off);

        public void Debug(string message)
        {
            Write(LogLevel.Debug, message);
        }

        public void Info(string message)
        {
            Write(LogLevel.Info, message);
        }

        public void Warning(string message)
        {
            Write(LogLevel.Warning, message);
        }

        public void Error(string message)
        {
            Write(LogLevel.Error, message);
        }

        public void Write(LogLevel level, string message)
        {
            if (level < _minimumLevel || level == LogLevel.Off)
            {
                return;
            }

            _sink.Log(level, $"[{DateTime.UtcNow:O}] [{level}] {message}");
        }

        private sealed class NullLog : ILog
        {
            public static readonly NullLog Instance = new NullLog();
            public void Log(LogLevel level, string message)
            {
            }
        }
    }
}
