namespace LogAnalyzer.Domain.Models;

public sealed class OperationalIncidentCandidate
{
    public string Severity { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;

    public string TechnicalSummary { get; set; } = string.Empty;
    public string PossibleRootCause { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = string.Empty;

    public double Confidence { get; set; }

    public string NormalizedOperationalTitle { get; set; } = string.Empty;

    public List<string> MatchingTerms { get; set; } = new();
}
