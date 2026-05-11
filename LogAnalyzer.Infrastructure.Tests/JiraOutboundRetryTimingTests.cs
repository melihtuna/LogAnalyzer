using LogAnalyzer.Infrastructure.Jira;

namespace LogAnalyzer.Infrastructure.Tests;

public sealed class JiraOutboundRetryTimingTests
{
    [Fact]
    public void ComputeBackoffCore_follows_exponential_curve_and_caps()
    {
        var options = new JiraOptions { RetryBaseMilliseconds = 400 };

        Assert.Equal(400d, JiraOutboundRetryTiming.ComputeBackoffCoreMilliseconds(options, failedAttemptIndex: 1));
        Assert.Equal(800d, JiraOutboundRetryTiming.ComputeBackoffCoreMilliseconds(options, failedAttemptIndex: 2));
        Assert.Equal(30_000d, JiraOutboundRetryTiming.ComputeBackoffCoreMilliseconds(options, failedAttemptIndex: 100));
    }

    [Fact]
    public void ComputeBackoffCore_zero_config_returns_zero_core()
    {
        var options = new JiraOptions { RetryBaseMilliseconds = 0 };
        Assert.Equal(0, JiraOutboundRetryTiming.ComputeBackoffCoreMilliseconds(options, failedAttemptIndex: 5));
        Assert.Equal(TimeSpan.Zero, JiraOutboundRetryTiming.ComputeDelay(options, failedAttemptIndex: 5));
    }
}
