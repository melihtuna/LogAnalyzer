using LogAnalyzer.Domain.Interfaces;
using LogAnalyzer.Domain.Models;
using LogAnalyzer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LogAnalyzer.Infrastructure.Repositories;

public class LogAnalysisRepository(LogAnalyzerDbContext dbContext) : ILogAnalysisRepository
{
    public Task<LogAnalysisRecord?> GetByHashAsync(string logHash, CancellationToken cancellationToken = default)
    {
        return dbContext.LogAnalyses
            .FirstOrDefaultAsync(x => x.LogHash == logHash, cancellationToken);
    }

    public async Task AddAsync(LogAnalysisRecord record, CancellationToken cancellationToken = default)
    {
        await dbContext.LogAnalyses.AddAsync(record, cancellationToken);
    }

    public void Update(LogAnalysisRecord record)
    {
        dbContext.LogAnalyses.Update(record);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
