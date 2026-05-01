using LogAnalyzer.Domain.Models;

namespace LogAnalyzer.Domain.Interfaces;

public interface ILogAnalysisRunRepository
{
    Task<IReadOnlyList<LogAnalysis>> GetLatestAsync(CancellationToken cancellationToken = default);

    Task AddAsync(LogAnalysis analysis, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

