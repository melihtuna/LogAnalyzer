using LogAnalyzer.Domain.Models;
using Microsoft.Extensions.Options;

namespace LogAnalyzer.Infrastructure.Jira;

/// <summary>
/// Phase 1 policy: integration toggle + idempotency on ExternalIssueKey only.
/// </summary>
public interface IJiraTicketEnqueuePolicy
{
    bool ShouldEnqueue(Incident incident);
}

public sealed class JiraTicketEnqueuePolicy(IOptions<JiraOptions> options) : IJiraTicketEnqueuePolicy
{
    private readonly JiraOptions _options = options.Value;

    public bool ShouldEnqueue(Incident incident)
    {
        if (!_options.EnableIntegration)
        {
            return false;
        }

        if (incident.Id == 0)
        {
            return false;
        }

        return string.IsNullOrEmpty(incident.ExternalIssueKey);
    }
}
