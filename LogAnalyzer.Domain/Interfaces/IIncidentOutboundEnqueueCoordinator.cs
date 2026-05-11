namespace LogAnalyzer.Domain.Interfaces;

/// <summary>
/// Scoped helper: tracks incident fingerprints touched during the current unit of work,
/// then resolves persisted incidents after SaveChanges and enqueues outbound jobs (e.g. Jira create).
/// </summary>
public interface IIncidentOutboundEnqueueCoordinator
{
    void TrackIncidentFingerprint(string incidentFingerprint);

    ValueTask FlushAfterPersistenceAsync(CancellationToken cancellationToken = default);
}
