namespace LogAnalyzer.Infrastructure.Persistence;

public sealed class BatchIncidentCandidatesCache
{
    public int Id { get; set; }

    public string BatchHash { get; set; } = string.Empty;

    public string ContractVersion { get; set; } = string.Empty;

    public string CandidatesJson { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
}
