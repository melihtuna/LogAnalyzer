namespace LogAnalyzer.Infrastructure.Jira;

/// <summary>
/// Shared backoff curve for REST retries (testable without mocking HttpClient timers).
/// </summary>
public static class JiraOutboundRetryTiming
{
    /// <summary>
    /// Exponential core before jitter is applied (milliseconds).
    /// </summary>
    public static double ComputeBackoffCoreMilliseconds(JiraOptions options, int failedAttemptIndex)
    {
        if (options.RetryBaseMilliseconds <= 0)
        {
            return 0;
        }

        var baseMs = Math.Max(50, options.RetryBaseMilliseconds);
        return Math.Min(baseMs * Math.Pow(2, failedAttemptIndex - 1), 30_000);
    }

    /// <summary>
    /// Computes delay before the next attempt after attempt <paramref name="failedAttemptIndex"/> failed (1-based).
    /// </summary>
    public static TimeSpan ComputeDelay(JiraOptions options, int failedAttemptIndex)
    {
        if (options.RetryBaseMilliseconds <= 0)
        {
            return TimeSpan.Zero;
        }

        var exponential = ComputeBackoffCoreMilliseconds(options, failedAttemptIndex);
        return TimeSpan.FromMilliseconds(exponential + Random.Shared.Next(0, 200));
    }
}
