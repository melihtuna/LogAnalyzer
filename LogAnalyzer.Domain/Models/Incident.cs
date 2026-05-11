namespace LogAnalyzer.Domain.Models;

public class Incident
{
    public int Id { get; set; }

    /// <summary>
    /// Deterministic hash from GroupId + algorithm version (see IIncidentFingerprintGenerator). Not globally unique in DB.
    /// </summary>
    public string IncidentFingerprint { get; set; } = string.Empty;

    public string FingerprintVersion { get; set; } = string.Empty;

    public string PrimaryGroupId { get; set; } = string.Empty;

    public string? PrimaryLogHash { get; set; }

    public IncidentStatus Status { get; set; }

    public DateTime FirstSeenUtc { get; set; }

    public DateTime LastSeenUtc { get; set; }

    public int OccurrenceCount { get; set; }

    public IncidentCategory Category { get; set; }

    public IncidentSeverity Severity { get; set; }

    public string TechnicalSummary { get; set; } = string.Empty;

    public string PossibleRootCause { get; set; } = string.Empty;

    public string RecommendedAction { get; set; } = string.Empty;

    public double Confidence { get; set; }

    public string AiModel { get; set; } = string.Empty;

    public string PromptVersion { get; set; } = string.Empty;

    public string PipelineVersion { get; set; } = string.Empty;

    public IncidentSource Source { get; set; }

    public DateTime UpdatedUtc { get; set; }

    public string? ExternalIssueKey { get; set; }

    public string? ExternalIssueUrl { get; set; }

    public ICollection<IncidentLogLink> LogLinks { get; set; } = new List<IncidentLogLink>();
}
