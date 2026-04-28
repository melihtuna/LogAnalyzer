using LogAnalyzer.Domain.Models;

namespace LogAnalyzer.Domain.Interfaces;

public interface ILogAnalysisRepository
{
    Task<LogAnalysisRecord?> GetByHashAsync(string logHash, CancellationToken cancellationToken = default);

    Task AddAsync(LogAnalysisRecord record, CancellationToken cancellationToken = default);

    void Update(LogAnalysisRecord record);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
