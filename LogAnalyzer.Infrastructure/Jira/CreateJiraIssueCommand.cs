namespace LogAnalyzer.Infrastructure.Jira;

public sealed record CreateJiraIssueCommand(int IncidentId, string Summary, string Description);
