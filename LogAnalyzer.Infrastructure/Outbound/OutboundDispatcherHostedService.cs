using System.Diagnostics;
using LogAnalyzer.Domain.Models;
using LogAnalyzer.Infrastructure.Jira;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LogAnalyzer.Infrastructure.Outbound;

public sealed class OutboundDispatcherHostedService(
    ChannelOutboundWorkQueue queue,
    JiraOutboundCreateExecutor createExecutor,
    OutboundIntegrationMetrics metrics,
    IOptions<JiraOptions> jiraOptions,
    ILogger<OutboundDispatcherHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!jiraOptions.Value.EnableIntegration)
        {
            logger.LogInformation("Jira outbound dispatcher idle because EnableIntegration is false.");
            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            return;
        }

        var concurrency = Math.Max(1, jiraOptions.Value.DispatcherConcurrency);
        var workers = new Task[concurrency];
        for (var i = 0; i < concurrency; i++)
        {
            workers[i] = ProcessLoopAsync(stoppingToken);
        }

        await Task.WhenAll(workers).ConfigureAwait(false);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        queue.CompleteWriter();

        var drained = queue.DrainUnreadCount();
        if (drained > 0)
        {
            logger.LogWarning("Outbound dispatcher shutdown discarded {DiscardedCount} pending work item(s)", drained);
        }
    }

    private async Task ProcessLoopAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var item in queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                metrics.RecordMessageDequeued();

                if (item.Kind != OutboundWorkKind.JiraCreateIssue)
                {
                    continue;
                }

                var sw = Stopwatch.StartNew();
                try
                {
                    await createExecutor.ExecuteAsync(item.IncidentId, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    metrics.RecordDispatchPoison(ex.GetType().Name, sw.Elapsed.TotalMilliseconds);
                    logger.LogError(
                        ex,
                        "Poison outbound work item — isolated so worker continues incident_id={IncidentId} kind={Kind}",
                        item.IncidentId,
                        item.Kind);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            metrics.RecordDispatcherReaderFault();
            logger.LogCritical(
                ex,
                "Outbound dispatcher reader terminated unexpectedly; worker exiting without consuming remaining items.");
        }
    }
}
