namespace LogAnalyzer.Domain.Models;

public class LogAnalysisRecord
{
    public int Id { get; set; }

    public string LogHash { get; set; } = string.Empty;

    public string GroupId { get; set; } = string.Empty;

    public string OriginalLog { get; set; } = string.Empty;

    public string ProcessedLog { get; set; } = string.Empty;

    public string Severity { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string Suggestion { get; set; } = string.Empty;

    public double Confidence { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public int Count { get; set; } = 1;

    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
}
