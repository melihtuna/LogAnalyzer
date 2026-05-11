using LogAnalyzer.Domain.Models;

namespace LogAnalyzer.Domain.Interfaces;

public interface IIncidentRepository
{
    Task<Incident?> FindActiveWithinWindowAsync(
        string incidentFingerprint,
        TimeSpan reuseWindow,
        CancellationToken cancellationToken = default);

    Task<Incident?> GetByIdAsync(int incidentId, CancellationToken cancellationToken = default);

    Task SetExternalIssueAsync(
        int incidentId,
        string externalIssueKey,
        string externalIssueUrl,
        CancellationToken cancellationToken = default);

    Task AddAsync(Incident incident, CancellationToken cancellationToken = default);
}
