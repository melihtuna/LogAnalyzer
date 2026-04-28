namespace LogAnalyzer.Domain.Models;

public class LogAnalysisResponse
{
    public string Severity { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string Suggestion { get; set; } = string.Empty;

    public double Confidence { get; set; }

    public string GroupId { get; set; } = string.Empty;

    public bool IsCached { get; set; }

    public bool IsLowConfidence { get; set; }

    public string? RawAIResponse { get; set; }
}
