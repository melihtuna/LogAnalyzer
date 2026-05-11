using LogAnalyzer.Domain.Interfaces;
using LogAnalyzer.Domain.Models;
using LogAnalyzer.Infrastructure.Jira;
using LogAnalyzer.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LogAnalyzer.Infrastructure.Outbound;

public sealed class IncidentOutboundEnqueueCoordinator(
    IOptions<JiraOptions> jiraOptions,
    IOptions<IncidentReuseOptions> reuseOptions,
    IIncidentRepository incidentRepository,
    IOutboundWorkQueue outboundWorkQueue,
    IJiraTicketEnqueuePolicy enqueuePolicy,
    OutboundIntegrationMetrics outboundMetrics,
    ILogger<IncidentOutboundEnqueueCoordinator> logger) : IIncidentOutboundEnqueueCoordinator
{
    private readonly object _gate = new();
    private readonly HashSet<string> _fingerprints = new(StringComparer.Ordinal);

    public void TrackIncidentFingerprint(string incidentFingerprint)
    {
        if (!jiraOptions.Value.EnableIntegration || string.IsNullOrWhiteSpace(incidentFingerprint))
        {
            return;
        }

        lock (_gate)
        {
            _fingerprints.Add(incidentFingerprint);
        }
    }

    public async ValueTask FlushAfterPersistenceAsync(CancellationToken cancellationToken = default)
    {
        if (!jiraOptions.Value.EnableIntegration)
        {
            return;
        }

        List<string> snapshot;
        lock (_gate)
        {
            snapshot = _fingerprints.ToList();
            _fingerprints.Clear();
        }

        if (snapshot.Count == 0)
        {
            return;
        }

        var window = TimeSpan.FromMinutes(Math.Max(reuseOptions.Value.ReuseWindowMinutes, 1));

        foreach (var fingerprint in snapshot.Distinct(StringComparer.Ordinal))
        {
            Incident? incident;
            try
            {
                incident = await incidentRepository
                    .FindActiveWithinWindowAsync(fingerprint, window, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                outboundMetrics.RecordEnqueueCoordinatorFault();
                logger.LogWarning(ex, "Outbound enqueue fingerprint lookup failed fingerprint={Fingerprint}", fingerprint);
                continue;
            }

            if (incident is null)
            {
                continue;
            }

            if (!enqueuePolicy.ShouldEnqueue(incident))
            {
                continue;
            }

            try
            {
                outboundWorkQueue.TryEnqueue(new OutboundWorkItem(OutboundWorkKind.JiraCreateIssue, incident.Id));
            }
            catch (Exception ex)
            {
                outboundMetrics.RecordEnqueueCoordinatorFault();
                logger.LogWarning(ex, "Outbound enqueue unexpected failure incident_id={IncidentId}", incident.Id);
            }
        }
    }
}
