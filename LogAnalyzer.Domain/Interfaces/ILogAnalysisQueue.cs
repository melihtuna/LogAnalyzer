using LogAnalyzer.Domain.Models;

namespace LogAnalyzer.Domain.Interfaces;

public interface ILogAnalysisQueue
{
    Task<LogAnalysisResponse> EnqueueAsync(LogRequest request, CancellationToken cancellationToken = default);

    Task<LogAnalysisResponse> EnqueueAsync(ILogProvider logProvider, bool includeRawAIResponse, CancellationToken cancellationToken = default);
}
