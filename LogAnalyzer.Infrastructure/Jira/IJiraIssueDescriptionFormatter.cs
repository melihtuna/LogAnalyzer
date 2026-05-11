using LogAnalyzer.Domain.Models;

namespace LogAnalyzer.Infrastructure.Jira;

/// <summary>
/// Stateless formatter from persisted incident fields to Jira issue command fragments.
/// </summary>
public interface IJiraIssueDescriptionFormatter
{
    CreateJiraIssueCommand Format(Incident incident);
}
