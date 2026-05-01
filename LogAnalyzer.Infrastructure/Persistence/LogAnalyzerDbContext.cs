using LogAnalyzer.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace LogAnalyzer.Infrastructure.Persistence;

public class LogAnalyzerDbContext(DbContextOptions<LogAnalyzerDbContext> options) : DbContext(options)
{
    private static readonly ValueConverter<DateTime, DateTime> UtcDateTimeConverter = new(
        value => value.ToUniversalTime(),
        value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static readonly ValueConverter<DateTimeOffset, DateTimeOffset> UtcDateTimeOffsetConverter = new(
        value => value.ToUniversalTime(),
        value => value.ToUniversalTime());

    public DbSet<LogAnalysisRecord> LogAnalyses => Set<LogAnalysisRecord>();
    public DbSet<LogAnalysis> LogAnalysisRuns => Set<LogAnalysis>();
    public DbSet<LogSourceCheckpoint> LogSourceCheckpoints => Set<LogSourceCheckpoint>();

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
            entity.Property(x => x.CreatedUtc).HasConversion(UtcDateTimeConverter).IsRequired();
            entity.Property(x => x.LastSeenUtc).HasConversion(UtcDateTimeConverter).IsRequired();
        });

        modelBuilder.Entity<LogAnalysis>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Timestamp).HasConversion(UtcDateTimeConverter).IsRequired();
            entity.Property(x => x.RawLogs).IsRequired();
            entity.Property(x => x.AnalysisResult).IsRequired();
        });

        modelBuilder.Entity<LogSourceCheckpoint>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Source).IsUnique();
            entity.Property(x => x.Source).HasMaxLength(64).IsRequired();
            entity.Property(x => x.LastProcessedTimestampUtc).HasConversion(UtcDateTimeOffsetConverter).IsRequired();
        });
    }
}
