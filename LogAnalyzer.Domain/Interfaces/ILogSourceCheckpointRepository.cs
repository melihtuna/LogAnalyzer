using LogAnalyzer.Domain.Models;

namespace LogAnalyzer.Domain.Interfaces;

public interface ILogSourceCheckpointRepository
{
    Task<LogSourceCheckpoint?> GetBySourceAsync(string source, CancellationToken cancellationToken = default);

    Task UpsertAsync(string source, DateTimeOffset timestampUtc, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

