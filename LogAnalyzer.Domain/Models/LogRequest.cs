namespace LogAnalyzer.Domain.Models;

public class LogRequest
{
    public string Logs { get; set; } = string.Empty;

    public bool IncludeRawAIResponse { get; set; }
}
