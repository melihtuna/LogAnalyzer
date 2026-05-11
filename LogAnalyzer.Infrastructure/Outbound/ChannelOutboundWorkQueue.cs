using System.Diagnostics;
using System.Threading.Channels;
using LogAnalyzer.Domain.Interfaces;
using LogAnalyzer.Domain.Models;
using LogAnalyzer.Domain.Observability;
using LogAnalyzer.Infrastructure.Jira;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LogAnalyzer.Infrastructure.Outbound;

public sealed class ChannelOutboundWorkQueue : IOutboundWorkQueue
{
    private readonly ChannelWriter<OutboundWorkItem> _writer;
    private readonly ChannelReader<OutboundWorkItem> _reader;
    private readonly ILogger<ChannelOutboundWorkQueue> _logger;
    private readonly IOptions<JiraOptions> _jiraOptions;
    private readonly OutboundIntegrationMetrics _metrics;
    private readonly int _capacity;

    public ChannelOutboundWorkQueue(
        IOptions<JiraOptions> jiraOptions,
        OutboundIntegrationMetrics metrics,
        ILogger<ChannelOutboundWorkQueue> logger)
    {
        _logger = logger;
        _jiraOptions = jiraOptions;
        _metrics = metrics;
        var opts = jiraOptions.Value;
        _capacity = Math.Max(1, opts.QueueCapacity);
        var concurrency = Math.Max(1, opts.DispatcherConcurrency);

        var channel = Channel.CreateBounded<OutboundWorkItem>(new BoundedChannelOptions(_capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = concurrency <= 1,
            SingleWriter = false
        });

        _writer = channel.Writer;
        _reader = channel.Reader;
    }

    public ChannelReader<OutboundWorkItem> Reader => _reader;

    public bool TryEnqueue(OutboundWorkItem item)
    {
        if (!_jiraOptions.Value.EnableIntegration)
        {
            _metrics.RecordEnqueueSkippedIntegrationDisabled();
            return false;
        }

        if (!_writer.TryWrite(item))
        {
            Activity.Current?.SetTag(ObservabilityAttributeKeys.OutboundQueueOverflow, true);
            Activity.Current?.SetTag(ObservabilityAttributeKeys.IncidentId, item.IncidentId);

            _metrics.RecordEnqueueRejectedQueueFull();

            _logger.LogWarning(
                "Outbound queue is full; skipping enqueue. capacity={Capacity} incident_id={IncidentId} kind={Kind}",
                _capacity,
                item.IncidentId,
                item.Kind);

            return false;
        }

        _metrics.RecordEnqueueAccepted();
        return true;
    }

    public void CompleteWriter()
    {
        _writer.TryComplete();
    }

    public int DrainUnreadCount()
    {
        var drained = 0;
        while (_reader.TryRead(out _))
        {
            drained++;
            _metrics.RecordMessageDequeued();
        }

        return drained;
    }
}
