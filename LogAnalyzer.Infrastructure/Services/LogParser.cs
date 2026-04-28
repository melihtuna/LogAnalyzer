using LogAnalyzer.Domain.Interfaces;

namespace LogAnalyzer.Infrastructure.Services;

public class LogParser : ILogParser
{
    public string ExtractErrorLinesOrFullLogs(string logs)
    {
        if (string.IsNullOrWhiteSpace(logs))
        {
            return string.Empty;
        }

        var lines = logs
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Where(line => line.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return lines.Length == 0 ? logs : string.Join(Environment.NewLine, lines);
    }
}
