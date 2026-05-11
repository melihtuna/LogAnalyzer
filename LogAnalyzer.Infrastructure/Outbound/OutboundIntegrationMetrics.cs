using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace LogAnalyzer.Infrastructure.Outbound;

public sealed class OutboundIntegrationMetrics : IDisposable
{
    public const string MeterName = "LogAnalyzer.Outbound";

    private readonly Meter _meter;
    private readonly Counter<long> _enqueueAccepted;
    private readonly Counter<long> _enqueueRejectedFull;
    private readonly Counter<long> _enqueueSkippedDisabled;
    private readonly Counter<long> _enqueueCoordinatorFault;
    private readonly Counter<long> _dispatchOutcome;
    private readonly Histogram<double> _dispatchDurationMs;
    private readonly Histogram<double> _jiraHttpDurationMs;
    private readonly Counter<long> _jiraRetryDecision;
    private readonly Counter<long> _jiraTerminalSignals;

    private long _approxQueueDepth;
    private long _lastAuthFailureUtcTicks;
    private long _lastRetryExhaustedUtcTicks;
    private long _dispatcherReaderFaultCount;

    public OutboundIntegrationMetrics()
    {
        _meter = new Meter(MeterName);

        _enqueueAccepted = _meter.CreateCounter<long>(
            "loganalyzer.outbound.enqueue.accepted",
            unit: "{msg}",
            description: "Accepted enqueue writes to the bounded outbound channel.");

        _enqueueRejectedFull = _meter.CreateCounter<long>(
            "loganalyzer.outbound.enqueue.rejected.queue_full",
            unit: "{msg}",
            description: "Enqueue attempts rejected because the queue was full.");

        _enqueueSkippedDisabled = _meter.CreateCounter<long>(
            "loganalyzer.outbound.enqueue.skipped.integration_disabled",
            unit: "{msg}",
            description: "Enqueue attempts skipped because integration was disabled.");

        _enqueueCoordinatorFault = _meter.CreateCounter<long>(
            "loganalyzer.outbound.enqueue.coordinator_fault",
            unit: "{event}",
            description: "Unexpected coordinator faults during enqueue.");

        _dispatchOutcome = _meter.CreateCounter<long>(
            "loganalyzer.outbound.dispatch.outcomes",
            unit: "{event}",
            description: "Dispatcher handling outcomes per work item.");

        _dispatchDurationMs = _meter.CreateHistogram<double>(
            "loganalyzer.outbound.dispatch.duration",
            unit: "ms",
            description: "Wall-clock time spent handling a dequeued outbound item.");

        _jiraHttpDurationMs = _meter.CreateHistogram<double>(
            "loganalyzer.outbound.jira.http.roundtrip.duration",
            unit: "ms",
            description: "HTTP round-trip latency for Jira create attempts.");

        _jiraRetryDecision = _meter.CreateCounter<long>(
            "loganalyzer.outbound.jira.retry.decisions",
            unit: "{decision}",
            description: "Retry classifier outcomes on non-success HTTP responses.");

        _jiraTerminalSignals = _meter.CreateCounter<long>(
            "loganalyzer.outbound.jira.terminal_signals",
            unit: "{signal}",
            description: "Terminal failure signals for ops dashboards.");

        _meter.CreateObservableGauge(
            "loganalyzer.outbound.queue.depth_approx",
            () => Math.Max(0, Interlocked.Read(ref _approxQueueDepth)),
            unit: "{msg}",
            description: "Approximate number of messages accepted into the queue but not yet dequeued.");
    }

    public long ApproxQueueDepthForHealth => Math.Max(0, Interlocked.Read(ref _approxQueueDepth));

    public long DispatcherReaderFaultCount => Interlocked.Read(ref _dispatcherReaderFaultCount);

    public void Dispose()
    {
        _meter.Dispose();
    }

    public void RecordEnqueueAccepted()
    {
        Interlocked.Increment(ref _approxQueueDepth);
        _enqueueAccepted.Add(1);
    }

    public void RecordEnqueueRejectedQueueFull()
    {
        _enqueueRejectedFull.Add(1);
    }

    public void RecordEnqueueSkippedIntegrationDisabled()
    {
        _enqueueSkippedDisabled.Add(1);
    }

    public void RecordEnqueueCoordinatorFault()
    {
        _enqueueCoordinatorFault.Add(1);
    }

    public void RecordMessageDequeued()
    {
        Interlocked.Decrement(ref _approxQueueDepth);
    }

    public void RecordDispatchOutcome(string outcome, double durationMilliseconds)
    {
        var tags = new TagList { { "outcome", outcome } };
        _dispatchOutcome.Add(1, tags);
        _dispatchDurationMs.Record(durationMilliseconds, tags);
    }

    public void RecordDispatchPoison(string exceptionType, double durationMilliseconds)
    {
        var tags = new TagList { { "outcome", "poison_exception" }, { "exception", exceptionType } };
        _dispatchOutcome.Add(1, tags);
        _dispatchDurationMs.Record(durationMilliseconds, tags);
    }

    public void RecordDispatcherReaderFault()
    {
        Interlocked.Increment(ref _dispatcherReaderFaultCount);
        _dispatchOutcome.Add(1, new TagList { { "outcome", "dispatcher_reader_fault" } });
    }

    public void RecordJiraHttpRoundTrip(double elapsedMilliseconds, bool success, int httpStatusCodeOrZero)
    {
        var tags = new TagList
        {
            { "success", success },
            { "http_status", httpStatusCodeOrZero },
        };
        _jiraHttpDurationMs.Record(elapsedMilliseconds, tags);
    }

    public void RecordJiraRetryDecision(string classification)
    {
        _jiraRetryDecision.Add(1, new TagList { { "classification", classification } });
    }

    public void RecordJiraAuthRejected()
    {
        Volatile.Write(ref _lastAuthFailureUtcTicks, DateTime.UtcNow.Ticks);
        _jiraTerminalSignals.Add(1, new TagList { { "signal", "auth_rejected" } });
    }

    public void RecordJiraRetryExhausted()
    {
        Volatile.Write(ref _lastRetryExhaustedUtcTicks, DateTime.UtcNow.Ticks);
        _jiraTerminalSignals.Add(1, new TagList { { "signal", "retry_exhausted" } });
    }

    public bool HasRecentAuthFailure(TimeSpan window)
    {
        var ticks = Volatile.Read(ref _lastAuthFailureUtcTicks);
        if (ticks == 0)
        {
            return false;
        }

        return DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc) < window;
    }

    public bool HasRecentRetryExhaustion(TimeSpan window)
    {
        var ticks = Volatile.Read(ref _lastRetryExhaustedUtcTicks);
        if (ticks == 0)
        {
            return false;
        }

        return DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc) < window;
    }
}
