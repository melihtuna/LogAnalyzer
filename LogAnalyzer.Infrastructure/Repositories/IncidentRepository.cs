using LogAnalyzer.Domain.Interfaces;
using LogAnalyzer.Domain.Models;
using LogAnalyzer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LogAnalyzer.Infrastructure.Repositories;

public sealed class IncidentRepository(LogAnalyzerDbContext dbContext) : IIncidentRepository
{
    public Task<Incident?> FindActiveWithinWindowAsync(
        string incidentFingerprint,
        TimeSpan reuseWindow,
        CancellationToken cancellationToken = default)
    {
        var threshold = DateTime.UtcNow.Subtract(reuseWindow);
        return dbContext.Incidents
            .Include(i => i.LogLinks)
            .Where(i =>
                i.IncidentFingerprint == incidentFingerprint
                && i.Status != IncidentStatus.Closed
                && i.LastSeenUtc >= threshold)
            .OrderByDescending(i => i.LastSeenUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<Incident?> GetByIdAsync(int incidentId, CancellationToken cancellationToken = default)
    {
        return dbContext.Incidents.AsNoTracking().FirstOrDefaultAsync(i => i.Id == incidentId, cancellationToken);
    }

    public Task SetExternalIssueAsync(
        int incidentId,
        string externalIssueKey,
        string externalIssueUrl,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return dbContext.Incidents
            .Where(i => i.Id == incidentId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(i => i.ExternalIssueKey, externalIssueKey)
                    .SetProperty(i => i.ExternalIssueUrl, externalIssueUrl)
                    .SetProperty(i => i.UpdatedUtc, now),
                cancellationToken);
    }

    public async Task AddAsync(Incident incident, CancellationToken cancellationToken = default)
    {
        await dbContext.Incidents.AddAsync(incident, cancellationToken);
    }
}
