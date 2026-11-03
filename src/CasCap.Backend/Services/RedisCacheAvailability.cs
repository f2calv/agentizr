namespace CasCap.Services;

/// <summary>Logs process-wide Redis cache availability transitions so per-call fallbacks do not repeat a Warning.</summary>
/// <remarks>
/// The first failure logs one Warning, repeats log at Debug, and the next successful cache call logs one
/// Information event with the failure count and duration. Cache error rates remain visible in metrics.
/// </remarks>
internal static partial class RedisCacheAvailability
{
    private static readonly Lock _lock = new();
    private static int _consecutiveFailures;
    private static long _failingSinceTimestamp;

    /// <summary>Records a failed cache call that fell back to PostgreSQL.</summary>
    /// <param name="logger">Logger of the calling store.</param>
    /// <param name="className">Name of the calling store.</param>
    /// <param name="timeProvider">Clock used to measure the failure duration.</param>
    public static void ReportFailure(ILogger logger, string className, TimeProvider timeProvider)
    {
        int consecutiveFailures;
        lock (_lock)
        {
            consecutiveFailures = ++_consecutiveFailures;
            if (consecutiveFailures == 1)
                _failingSinceTimestamp = timeProvider.GetTimestamp();
        }
        if (consecutiveFailures == 1)
            LogCacheFailing(logger, className);
        else
            LogCacheStillFailing(logger, className, consecutiveFailures);
    }

    /// <summary>Records a successful cache call and logs recovery after a failure.</summary>
    /// <param name="logger">Logger of the calling store.</param>
    /// <param name="className">Name of the calling store.</param>
    /// <param name="timeProvider">Clock used to measure the failure duration.</param>
    public static void ReportSuccess(ILogger logger, string className, TimeProvider timeProvider)
    {
        if (Volatile.Read(ref _consecutiveFailures) == 0)
            return;
        int consecutiveFailures;
        long failingSinceTimestamp;
        lock (_lock)
        {
            consecutiveFailures = _consecutiveFailures;
            failingSinceTimestamp = _failingSinceTimestamp;
            _consecutiveFailures = 0;
        }
        if (consecutiveFailures > 0)
            LogCacheRecovered(logger, className, consecutiveFailures, timeProvider.GetElapsedTime(failingSinceTimestamp));
    }

    [LoggerMessage(LogLevel.Warning, "{ClassName} Redis cache unavailable; using PostgreSQL")]
    private static partial void LogCacheFailing(ILogger logger, string className);

    [LoggerMessage(LogLevel.Debug, "{ClassName} Redis cache still unavailable ConsecutiveFailures={ConsecutiveFailures}")]
    private static partial void LogCacheStillFailing(ILogger logger, string className, int consecutiveFailures);

    [LoggerMessage(LogLevel.Information, "{ClassName} Redis cache recovered ConsecutiveFailures={ConsecutiveFailures} Downtime={Downtime}")]
    private static partial void LogCacheRecovered(ILogger logger, string className, int consecutiveFailures, TimeSpan downtime);
}
