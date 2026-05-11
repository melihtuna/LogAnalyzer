namespace LogAnalyzer.Infrastructure.Jira;

/// <summary>
/// Infrastructure-only port for creating Jira issues (Phase 1: mock or REST).
/// </summary>
public interface IJiraIssueService
{
    Task<CreateJiraIssueResult> CreateIssueAsync(CreateJiraIssueCommand command, CancellationToken cancellationToken);
}
