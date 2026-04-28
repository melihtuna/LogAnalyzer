namespace LogAnalyzer.Domain.Interfaces;

public interface ILogParser
{
    string ExtractErrorLinesOrFullLogs(string logs);
}
