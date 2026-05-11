namespace LogAnalyzer.Infrastructure.Jira;

public sealed class JiraOptions
{
    public const string SectionName = "Jira";

    public bool EnableIntegration { get; set; }

    public int QueueCapacity { get; set; } = 256;

    public int DispatcherConcurrency { get; set; } = 1;

    public bool UseMockClient { get; set; } = true;

    public string BaseUrl { get; set; } = "https://example.atlassian.net";

    public string ProjectKey { get; set; } = "LOG";

    public string IssueTypeName { get; set; } = "Task";

    public int RequestTimeoutSeconds { get; set; } = 60;

    public int MaxRetries { get; set; } = 3;

    public int RetryBaseMilliseconds { get; set; } = 400;

    public JiraAuthKind AuthKind { get; set; } = JiraAuthKind.BasicEmailApiToken;

    public string? Email { get; set; }

    public string? ApiToken { get; set; }

    public string? PersonalAccessToken { get; set; }

    public double HealthDegradedQueueFillRatio { get; set; } = 0.85;
}
