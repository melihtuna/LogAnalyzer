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
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<IncidentLogLink> IncidentLogLinks => Set<IncidentLogLink>();

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
            entity.Property(x => x.PossibleRootCause).IsRequired();
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

        modelBuilder.Entity<Incident>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.IncidentFingerprint);
            entity.HasIndex(x => new { x.IncidentFingerprint, x.Status, x.LastSeenUtc });
            entity.Property(x => x.IncidentFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(x => x.FingerprintVersion).HasMaxLength(16).IsRequired();
            entity.Property(x => x.PrimaryGroupId).HasMaxLength(64).IsRequired();
            entity.Property(x => x.PrimaryLogHash).HasMaxLength(128);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(x => x.Category).HasConversion<string>().HasMaxLength(64).IsRequired();
            entity.Property(x => x.Severity).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(x => x.Source).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(x => x.TechnicalSummary).IsRequired();
            entity.Property(x => x.PossibleRootCause).IsRequired();
            entity.Property(x => x.RecommendedAction).IsRequired();
            entity.Property(x => x.AiModel).HasMaxLength(128).IsRequired();
            entity.Property(x => x.PromptVersion).HasMaxLength(64).IsRequired();
            entity.Property(x => x.PipelineVersion).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ExternalIssueKey).HasMaxLength(64);
            entity.Property(x => x.ExternalIssueUrl).HasMaxLength(512);
            entity.Property(x => x.FirstSeenUtc).HasConversion(UtcDateTimeConverter).IsRequired();
            entity.Property(x => x.LastSeenUtc).HasConversion(UtcDateTimeConverter).IsRequired();
            entity.Property(x => x.UpdatedUtc).HasConversion(UtcDateTimeConverter).IsRequired();
        });

        modelBuilder.Entity<IncidentLogLink>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.IncidentId, x.LogHash }).IsUnique();
            entity.Property(x => x.LogHash).HasMaxLength(128).IsRequired();
            entity.Property(x => x.LinkedUtc).HasConversion(UtcDateTimeConverter).IsRequired();
            entity.HasOne(x => x.Incident)
                .WithMany(i => i.LogLinks)
                .HasForeignKey(x => x.IncidentId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
