using LogAnalyzer.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace LogAnalyzer.Infrastructure.Persistence;

public class LogAnalyzerDbContext(DbContextOptions<LogAnalyzerDbContext> options) : DbContext(options)
{
    public DbSet<LogAnalysisRecord> LogAnalyses => Set<LogAnalysisRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LogAnalysisRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.LogHash).IsUnique();
            entity.Property(x => x.LogHash).HasMaxLength(128).IsRequired();
            entity.Property(x => x.GroupId).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Severity).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Category).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Summary).IsRequired();
            entity.Property(x => x.Suggestion).IsRequired();
            entity.Property(x => x.Count).HasDefaultValue(1).IsRequired();
            entity.Property(x => x.LastSeenUtc).IsRequired();
        });
    }
}
