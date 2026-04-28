using LogAnalyzer.Domain.Interfaces;
using LogAnalyzer.Domain.Models;

namespace LogAnalyzer.Infrastructure.Notifications;

public class NullNotificationService : INotificationService
{
    public Task NotifyCriticalAsync(LogAnalysisRecord record, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
