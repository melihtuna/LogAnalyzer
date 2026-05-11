using LogAnalyzer.Domain.Models;

namespace LogAnalyzer.Domain.Interfaces;

public interface IBatchIncidentCandidatesRepository
{
    Task<LogAnalysisBatchCandidatesResult?> GetByBatchHashAsync(
        string batchHash,
        string contractVersion,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(
        string batchHash,
        string contractVersion,
        LogAnalysisBatchCandidatesResult candidatesResult,
        CancellationToken cancellationToken = default);
}

