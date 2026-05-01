using LogAnalyzer.Domain.Interfaces;
using LogAnalyzer.Domain.Models;
using LogAnalyzer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LogAnalyzer.Infrastructure.Repositories;

public class LogSourceCheckpointRepository(LogAnalyzerDbContext dbContext) : ILogSourceCheckpointRepository
{
    public Task<LogSourceCheckpoint?> GetBySourceAsync(string source, CancellationToken cancellationToken = default)
    {
        return dbContext.LogSourceCheckpoints
            .Select(x => new LogSourceCheckpoint
            {
                Id = x.Id,
                Source = x.Source,
                LastProcessedTimestampUtc = x.LastProcessedTimestampUtc.ToUniversalTime()
            })
            .FirstOrDefaultAsync(x => x.Source == source, cancellationToken);
    }

    public async Task UpsertAsync(string source, DateTimeOffset timestampUtc, CancellationToken cancellationToken = default)
    {
        var normalizedTimestampUtc = timestampUtc.ToUniversalTime();
        var existing = await dbContext.LogSourceCheckpoints
            .FirstOrDefaultAsync(x => x.Source == source, cancellationToken);

        if (existing is null)
        {
            await dbContext.LogSourceCheckpoints.AddAsync(new LogSourceCheckpoint
            {
                Source = source,
                LastProcessedTimestampUtc = normalizedTimestampUtc
            }, cancellationToken);
            return;
        }

        existing.LastProcessedTimestampUtc = normalizedTimestampUtc;
        dbContext.LogSourceCheckpoints.Update(existing);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}

