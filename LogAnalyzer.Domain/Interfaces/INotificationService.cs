using LogAnalyzer.Domain.Models;

namespace LogAnalyzer.Domain.Interfaces;

public interface INotificationService
{
    Task NotifyCriticalAsync(LogAnalysisRecord record, CancellationToken cancellationToken = default);
}
