using System.Text.Json;
using LogAnalyzer.Domain.Interfaces;
using LogAnalyzer.Domain.Models;
using LogAnalyzer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LogAnalyzer.Infrastructure.Repositories;

public sealed class BatchIncidentCandidatesRepository(
    LogAnalyzerDbContext dbContext) : IBatchIncidentCandidatesRepository
{
    public async Task<LogAnalysisBatchCandidatesResult?> GetByBatchHashAsync(
        string batchHash,
        string contractVersion,
        CancellationToken cancellationToken = default)
    {
        var cache = await dbContext.BatchIncidentCandidates
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.BatchHash == batchHash && x.ContractVersion == contractVersion,
                cancellationToken)
            .ConfigureAwait(false);

        if (cache is null)
        {
            return null;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<LogAnalysisBatchCandidatesResult>(cache.CandidatesJson);
            return parsed;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task UpsertAsync(
        string batchHash,
        string contractVersion,
        LogAnalysisBatchCandidatesResult candidatesResult,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidatesResult);

        var existing = await dbContext.BatchIncidentCandidates
            .FirstOrDefaultAsync(
                x => x.BatchHash == batchHash && x.ContractVersion == contractVersion,
                cancellationToken)
            .ConfigureAwait(false);

        var now = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(candidatesResult);

        if (existing is null)
        {
            dbContext.BatchIncidentCandidates.Add(new BatchIncidentCandidatesCache
            {
                BatchHash = batchHash,
                ContractVersion = contractVersion,
                CandidatesJson = json,
                CreatedUtc = now,
                LastSeenUtc = now
            });
            return;
        }

        existing.CandidatesJson = json;
        existing.LastSeenUtc = now;
    }
}

