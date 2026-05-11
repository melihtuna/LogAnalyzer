namespace LogAnalyzer.Domain.Models;

public sealed record IncidentUpsertPresentation(
    string? OperationalTitle = null,
    string? EvidenceLogExcerpt = null);
