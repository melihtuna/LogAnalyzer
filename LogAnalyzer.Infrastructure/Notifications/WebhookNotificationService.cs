using System.Net.Http.Json;
using LogAnalyzer.Domain.Interfaces;
using LogAnalyzer.Domain.Models;
using LogAnalyzer.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LogAnalyzer.Infrastructure.Notifications;

public class WebhookNotificationService(
    HttpClient httpClient,
    IOptions<WebhookOptions> options,
    ILogger<WebhookNotificationService> logger) : INotificationService
{
    public async Task NotifyCriticalAsync(LogAnalysisRecord record, CancellationToken cancellationToken = default)
    {
        var webhookUrl = options.Value.Url;
        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            logger.LogDebug("Skipping critical notification because no webhook URL is configured.");
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var payload = new
                {
                    record.GroupId,
                    record.Severity,
                    record.Category,
                    record.Summary,
                    record.Suggestion,
                    record.Confidence,
                    record.CreatedUtc,
                    record.Count,
                    record.LastSeenUtc
                };

                using var response = await httpClient.PostAsJsonAsync(webhookUrl, payload, CancellationToken.None);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Critical notification webhook failed.");
            }
        }, CancellationToken.None);

        await Task.CompletedTask;
    }
}
