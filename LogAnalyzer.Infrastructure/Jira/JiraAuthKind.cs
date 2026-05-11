namespace LogAnalyzer.Infrastructure.Jira;

public enum JiraAuthKind
{
    BasicEmailApiToken = 0,

    /// <summary>Personal access token (Bearer), when supported by the deployment.</summary>
    BearerPat = 1,
}
