using System.Diagnostics;
using LogAnalyzer.Domain.Interfaces;
using LogAnalyzer.Domain.Observability;
using LogAnalyzer.Infrastructure.Jira;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LogAnalyzer.Infrastructure.Outbound;

public sealed class JiraOutboundCreateExecutor(
    IServiceScopeFactory scopeFactory,
    IJiraIssueDescriptionFormatter formatter,
    IJiraTicketEnqueuePolicy enqueuePolicy,
    OutboundIntegrationMetrics metrics,
    ILogger<JiraOutboundCreateExecutor> logger)
{
    private static readonly ActivitySource ActivitySource = new("LogAnalyzer.Outbound");

    public async Task ExecuteAsync(int incidentId, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("jira.dispatch.create");
        activity?.SetTag(ObservabilityAttributeKeys.IncidentId, incidentId);
        activity?.SetTag(ObservabilityAttributeKeys.JiraOperation, "create_issue");

        var sw = Stopwatch.StartNew();
        try
        {
            using var scope = scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IIncidentRepository>();
            var jiraIssueService = scope.ServiceProvider.GetRequiredService<IJiraIssueService>();

            var incident = await repo.GetByIdAsync(incidentId, cancellationToken).ConfigureAwait(false);
            if (incident is null)
            {
                activity?.SetTag(ObservabilityAttributeKeys.JiraDispatchOutcome, "incident_missing");
                logger.LogWarning("Outbound Jira create skipped: incident not found incident_id={IncidentId}", incidentId);
                metrics.RecordDispatchOutcome("incident_missing", sw.Elapsed.TotalMilliseconds);
                return;
            }

            if (!enqueuePolicy.ShouldEnqueue(incident))
            {
                activity?.SetTag(ObservabilityAttributeKeys.JiraDispatchOutcome, "skipped_idempotent_or_disabled");
                logger.LogDebug("Outbound Jira create skipped incident_id={IncidentId}", incidentId);
                metrics.RecordDispatchOutcome("skipped_idempotent_or_disabled", sw.Elapsed.TotalMilliseconds);
                return;
            }

            var command = formatter.Format(incident);
            var result = await jiraIssueService.CreateIssueAsync(command, cancellationToken).ConfigureAwait(false);

            await repo.SetExternalIssueAsync(incident.Id, result.IssueKey, result.BrowseUrl, cancellationToken)
                .ConfigureAwait(false);

            activity?.SetTag(ObservabilityAttributeKeys.JiraIssueKey, result.IssueKey);
            activity?.SetTag(ObservabilityAttributeKeys.JiraDispatchOutcome, "created");

            metrics.RecordDispatchOutcome("created", sw.Elapsed.TotalMilliseconds);

            logger.LogInformation(
                "Jira issue linked incident_id={IncidentId} issue_key={IssueKey}",
                incident.Id,
                result.IssueKey);
        }
        catch (OperationCanceledException)
        {
            activity?.SetTag(ObservabilityAttributeKeys.JiraDispatchOutcome, "cancelled");
            metrics.RecordDispatchOutcome("cancelled", sw.Elapsed.TotalMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            activity?.SetTag(ObservabilityAttributeKeys.JiraDispatchOutcome, "failed");
            metrics.RecordDispatchOutcome("failed", sw.Elapsed.TotalMilliseconds);
            logger.LogError(ex, "Outbound Jira create failed incident_id={IncidentId}", incidentId);
        }
    }
}
