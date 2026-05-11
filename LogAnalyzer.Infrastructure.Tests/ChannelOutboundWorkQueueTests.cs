using LogAnalyzer.Domain.Models;
using LogAnalyzer.Infrastructure.Jira;
using LogAnalyzer.Infrastructure.Outbound;
using Microsoft.Extensions.Logging.Abstractions;
namespace LogAnalyzer.Infrastructure.Tests;

public sealed class ChannelOutboundWorkQueueTests
{
    [Fact]
    public void TryEnqueue_returns_false_when_full_without_throwing()
    {
        using var metrics = new OutboundIntegrationMetrics();
        var opts = Microsoft.Extensions.Options.Options.Create(new JiraOptions
        {
            EnableIntegration = true,
            QueueCapacity = 2,
            DispatcherConcurrency = 1,
        });

        var queue = new ChannelOutboundWorkQueue(opts, metrics, NullLogger<ChannelOutboundWorkQueue>.Instance);

        Assert.True(queue.TryEnqueue(new OutboundWorkItem(OutboundWorkKind.JiraCreateIssue, 1)));
        Assert.True(queue.TryEnqueue(new OutboundWorkItem(OutboundWorkKind.JiraCreateIssue, 2)));
        Assert.False(queue.TryEnqueue(new OutboundWorkItem(OutboundWorkKind.JiraCreateIssue, 3)));
    }

    [Fact]
    public void TryEnqueue_returns_false_when_integration_disabled()
    {
        using var metrics = new OutboundIntegrationMetrics();
        var opts = Microsoft.Extensions.Options.Options.Create(new JiraOptions
        {
            EnableIntegration = false,
            QueueCapacity = 10,
        });

        var queue = new ChannelOutboundWorkQueue(opts, metrics, NullLogger<ChannelOutboundWorkQueue>.Instance);

        Assert.False(queue.TryEnqueue(new OutboundWorkItem(OutboundWorkKind.JiraCreateIssue, 1)));
    }
}
