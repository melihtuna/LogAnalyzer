using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LogAnalyzer.Infrastructure.Jira;

public sealed class MockJiraIssueService(IOptions<JiraOptions> options, ILogger<MockJiraIssueService> logger)
    : IJiraIssueService
{
    private readonly JiraOptions _options = options.Value;

    public Task<CreateJiraIssueResult> CreateIssueAsync(CreateJiraIssueCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var project = string.IsNullOrWhiteSpace(_options.ProjectKey) ? "MOCK" : _options.ProjectKey.Trim();
        var key = $"{project}-{command.IncidentId}";
        var baseUrl = _options.BaseUrl.TrimEnd('/');
        var url = $"{baseUrl}/browse/{key}";

        logger.LogInformation(
            "Mock Jira issue created incident_id={IncidentId} issue_key={IssueKey} summary_length={SummaryLength}",
            command.IncidentId,
            key,
            command.Summary.Length);

        return Task.FromResult(new CreateJiraIssueResult(key, url));
    }
}
