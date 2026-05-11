using LogAnalyzer.Domain.Models;

namespace LogAnalyzer.Domain.Interfaces;

public interface IIncidentUpsertService
{
    /// <param name="occurrenceIncrement">How many occurrences to add for this upsert (e.g. lines in a batch).</param>
    Task<IncidentUpsertResult> UpsertFromLogAnalysisAsync(
        LogAnalysisRecord record,
        IncidentSource source,
        int occurrenceIncrement = 1,
        ClassificationCorrelationSnapshot? classificationCorrelation = null,
        IncidentUpsertPresentation? presentation = null,
        CancellationToken cancellationToken = default);
}

public sealed record IncidentUpsertResult(string IncidentFingerprint, bool WasCreated);
