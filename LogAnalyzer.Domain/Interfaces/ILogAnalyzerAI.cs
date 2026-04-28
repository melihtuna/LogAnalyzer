using LogAnalyzer.Domain.Models;

namespace LogAnalyzer.Domain.Interfaces;

public interface ILogAnalyzerAI
{
    Task<LogAnalysisResult> AnalyzeAsync(string log, CancellationToken cancellationToken = default);
}
