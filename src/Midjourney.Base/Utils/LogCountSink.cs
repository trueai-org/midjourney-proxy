using Serilog.Core;
using Serilog.Events;

namespace Midjourney.Base.Utils
{
    /// <summary>
    /// 日志计数器 - 每日重置
    /// </summary>
    public class LogCountSink : ILogEventSink
    {
        // 日统计
        private static int _toDayLogCount = 0;

        private static int _toDayErrorLogCount = 0;
        private static int _toDayWarningLogCount = 0;
        private static DateTime _todayLastResetDate = DateTime.Now.Date;

        // 小时统计
        private static int _toHourLogCount = 0;

        private static int _toHourErrorLogCount = 0;
        private static int _toHourWarningLogCount = 0;
        private static DateTime _toHourLastResetDate = DateTime.Now;

        public static int ToDayLogCount => _toDayLogCount;
        public static int ToDayErrorLogCount => _toDayErrorLogCount;
        public static int ToDayWarningLogCount => _toDayWarningLogCount;

        public static int ToHourLogCount => _toHourLogCount;
        public static int ToHourErrorLogCount => _toHourErrorLogCount;
        public static int ToHourWarningLogCount => _toHourWarningLogCount;

        /// <summary>
        /// 如果需要，重置计数器
        /// </summary>
        public static void ResetCountsIfNeeded()
        {
            var currentDate = DateTime.Now.Date;
            if (currentDate > _todayLastResetDate)
            {
                Interlocked.Exchange(ref _toDayLogCount, 0);
                Interlocked.Exchange(ref _toDayErrorLogCount, 0);
                Interlocked.Exchange(ref _toDayWarningLogCount, 0);
                _todayLastResetDate = currentDate;
            }

            var currentHour = DateTime.Now;
            if (currentHour.Hour != _toHourLastResetDate.Hour)
            {
                Interlocked.Exchange(ref _toHourLogCount, 0);
                Interlocked.Exchange(ref _toHourErrorLogCount, 0);
                Interlocked.Exchange(ref _toHourWarningLogCount, 0);
                _toHourLastResetDate = currentHour;
            }
        }

        /// <summary>
        /// 记录日志事件
        /// </summary>
        /// <param name="logEvent"></param>
        public void Emit(LogEvent logEvent)
        {
            ResetCountsIfNeeded();

            Interlocked.Increment(ref _toDayLogCount);
            Interlocked.Increment(ref _toHourLogCount);

            if (logEvent.Level == LogEventLevel.Error || logEvent.Level == LogEventLevel.Fatal)
            {
                Interlocked.Increment(ref _toDayErrorLogCount);
                Interlocked.Increment(ref _toHourErrorLogCount);
            }
            else if (logEvent.Level == LogEventLevel.Warning)
            {
                Interlocked.Increment(ref _toDayWarningLogCount);
                Interlocked.Increment(ref _toHourWarningLogCount);
            }
        }
    }
}