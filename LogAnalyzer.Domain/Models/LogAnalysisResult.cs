namespace LogAnalyzer.Domain.Models;

public class LogAnalysisResult
{
    public string Severity { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string Suggestion { get; set; } = string.Empty;

    public double Confidence { get; set; }
}
