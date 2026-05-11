namespace LogAnalyzer.Domain.Models;

public sealed class LogAnalysisBatchCandidatesResult
{
    public string SchemaVersion { get; set; } = string.Empty;
    public List<OperationalIncidentCandidate> Candidates { get; set; } = new();

    public string RawAIResponse { get; set; } = string.Empty;

    public string ClassificationParseMode { get; set; } = string.Empty;

    public string ClassificationFallbackUsed { get; set; } = string.Empty;

    public int ClassificationRetryCount { get; set; }
}
