using LogAnalyzer.Infrastructure.Jira;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace LogAnalyzer.Infrastructure.Outbound;

public sealed class OutboundIntegrationHealthCheck(
    IOptions<JiraOptions> jiraOptions,
    OutboundIntegrationMetrics metrics) : IHealthCheck
{
    private static readonly TimeSpan RecentSignalWindow = TimeSpan.FromMinutes(5);

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var opts = jiraOptions.Value;

        var data = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["integration_enabled"] = opts.EnableIntegration,
            ["use_mock_client"] = opts.UseMockClient,
            ["approx_queue_depth"] = metrics.ApproxQueueDepthForHealth,
            ["queue_capacity"] = Math.Max(1, opts.QueueCapacity),
            ["dispatcher_reader_fault_total"] = metrics.DispatcherReaderFaultCount,
        };

        if (!opts.EnableIntegration)
        {
            return Task.FromResult(
                HealthCheckResult.Healthy("Jira outbound integration disabled.", data: data));
        }

        var capacity = Math.Max(1, opts.QueueCapacity);
        var depth = metrics.ApproxQueueDepthForHealth;
        var ratio = depth / (double)capacity;
        data["queue_fill_ratio"] = ratio;

        var degradedRatio = Math.Clamp(opts.HealthDegradedQueueFillRatio <= 0 ? 0.85 : opts.HealthDegradedQueueFillRatio, 0.05, 1.0);
        if (ratio >= degradedRatio)
        {
            data["degraded_reason"] = "queue_near_capacity";
            return Task.FromResult(
                HealthCheckResult.Degraded($"Outbound queue pressure high ({depth}/{capacity}).", data: data));
        }

        if (metrics.HasRecentAuthFailure(RecentSignalWindow))
        {
            data["degraded_reason"] = "jira_auth_rejected_recent";
            return Task.FromResult(
                HealthCheckResult.Degraded("Recent Jira HTTP 401/403-style auth rejection observed.", data: data));
        }

        if (metrics.HasRecentRetryExhaustion(RecentSignalWindow))
        {
            data["degraded_reason"] = "jira_retry_exhausted_recent";
            return Task.FromResult(
                HealthCheckResult.Degraded("Recent Jira retry exhaustion observed.", data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy("Outbound integration nominal.", data: data));
    }
}
