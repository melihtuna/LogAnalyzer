using LogAnalyzer.Domain.Interfaces;
using LogAnalyzer.Domain.Models;
using LogAnalyzer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LogAnalyzer.Infrastructure.Repositories;

public class LogAnalysisRunRepository(LogAnalyzerDbContext dbContext) : ILogAnalysisRunRepository
{
    public async Task<IReadOnlyList<LogAnalysis>> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.LogAnalysisRuns
            .AsNoTracking()
            .OrderByDescending(x => x.Timestamp)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(LogAnalysis analysis, CancellationToken cancellationToken = default)
    {
        await dbContext.LogAnalysisRuns.AddAsync(analysis, cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}

