namespace LogAnalyzer.Domain.Providers;

public class StaticLogProvider(string logs) : LogAnalyzer.Domain.Interfaces.ILogProvider
{
    public Task<string> GetLogsAsync()
    {
        return Task.FromResult(logs);
    }
}

