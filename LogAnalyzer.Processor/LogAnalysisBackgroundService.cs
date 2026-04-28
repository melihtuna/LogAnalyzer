using System.Threading.Channels;
using LogAnalyzer.Domain.Interfaces;
using LogAnalyzer.Processor.Queue;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LogAnalyzer.Processor;

public class LogAnalysisBackgroundService(
    Channel<QueuedLogAnalysisRequest> channel,
    IServiceScopeFactory scopeFactory,
    ILogger<LogAnalysisBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var orchestrator = scope.ServiceProvider.GetRequiredService<ILogAnalysisOrchestrator>();
                var result = await orchestrator.AnalyzeAsync(item.Request, item.CancellationToken);
                item.CompletionSource.TrySetResult(result);
            }
            catch (OperationCanceledException ex)
            {
                item.CompletionSource.TrySetCanceled(ex.CancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Queued log analysis failed.");
                item.CompletionSource.TrySetException(ex);
            }
        }
    }
}
