using LogAnalyzer.Domain.Models;

namespace LogAnalyzer.Domain.Interfaces;

public interface ILogAnalysisOrchestrator
{
    Task<LogAnalysisResponse> AnalyzeAsync(LogRequest request, CancellationToken cancellationToken = default);
}
