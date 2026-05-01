namespace LogAnalyzer.Domain.Models;

public class LogSourceCheckpoint
{
    public int Id { get; set; }

    public string Source { get; set; } = string.Empty;

    public DateTimeOffset LastProcessedTimestampUtc { get; set; }
}

