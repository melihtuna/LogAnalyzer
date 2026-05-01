namespace LogAnalyzer.Domain.Interfaces;

public interface ILogProvider
{
    Task<string> GetLogsAsync();
}

