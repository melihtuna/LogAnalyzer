namespace LogAnalyzer.Infrastructure.Jira;

/// <summary>
/// Fixed caps for deterministic Jira field shaping (independent of runtime config).
/// </summary>
public static class JiraIssueFormattingLimits
{
    public const int SummaryMaxLength = 160;

    /// <summary>Ellipsis appended when summary is truncated (ASCII).</summary>
    public const string SummaryEllipsis = "...";

    public const int MetadataSectionMaxLength = 512;

    public const int TechnicalSummaryMaxLength = 12_000;

    public const int EvidenceLogExcerptMaxLength = 4_000;

    public const int PossibleRootCauseMaxLength = 8_000;

    public const int RecommendedActionMaxLength = 8_000;

    public const int AuditLinesMaxLength = 2_000;
}
