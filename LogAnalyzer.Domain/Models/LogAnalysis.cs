namespace LogAnalyzer.Domain.Models;

public class LogAnalysis
{
    public int Id { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public string RawLogs { get; set; } = string.Empty;

    public string AnalysisResult { get; set; } = string.Empty;
}

