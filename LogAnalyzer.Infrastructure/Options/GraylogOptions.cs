namespace LogAnalyzer.Infrastructure.Options;

public class GraylogOptions
{
    public const string SectionName = "Graylog";

    public string BaseUrl { get; set; } = string.Empty;

    public string ApiToken { get; set; } = string.Empty;

    public string Query { get; set; } = "level:ERROR OR level:WARN";

    public int TimeRangeMinutes { get; set; } = 5;

    public int PageSize { get; set; } = 250;

    public int MaxLogsPerCycle { get; set; } = 2000;

    public int RequestTimeoutSeconds { get; set; } = 20;

    public int MaxRetries { get; set; } = 3;

    public int RetryDelayMilliseconds { get; set; } = 500;
}

