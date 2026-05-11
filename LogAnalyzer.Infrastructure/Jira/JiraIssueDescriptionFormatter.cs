using System.Collections.Generic;
using System.Globalization;
using LogAnalyzer.Domain.Models;

namespace LogAnalyzer.Infrastructure.Jira;

/// <summary>
/// Deterministic section ordering and truncation rules for Phase 1 create payloads.
/// </summary>
public sealed class JiraIssueDescriptionFormatter : IJiraIssueDescriptionFormatter
{
    internal static readonly string[] SummarySectionOrder =
    [
        "Incident metadata",
        "Operational title",
        "Evidence (scoped log excerpt)",
        "AI technical summary",
        "Possible root cause",
        "Recommended action",
        "Classification / pipeline audit",
    ];

    public CreateJiraIssueCommand Format(Incident incident)
    {
        ArgumentNullException.ThrowIfNull(incident);

        var summary = NormalizeSummary(incident);
        var description = BuildPlainDescription(incident);

        return new CreateJiraIssueCommand(incident.Id, summary, description);
    }

    internal static string NormalizeSummary(Incident incident)
    {
        var raw = !string.IsNullOrWhiteSpace(incident.OperationalTitle)
            ? incident.OperationalTitle.Trim()
            : string.IsNullOrWhiteSpace(incident.TechnicalSummary)
                ? FormattableString.Invariant($"Incident {incident.Id}")
                : incident.TechnicalSummary.Trim();

        raw = CollapseWhitespace(raw);

        var max = JiraIssueFormattingLimits.SummaryMaxLength;
        var ellipsis = JiraIssueFormattingLimits.SummaryEllipsis;
        if (raw.Length <= max)
        {
            return raw;
        }

        var take = Math.Max(0, max - ellipsis.Length);
        return string.Concat(raw.AsSpan(0, take), ellipsis);
    }

    internal static string BuildPlainDescription(Incident incident)
    {
        var confidenceText = incident.Confidence.ToString("0.###", CultureInfo.InvariantCulture);

        var meta =
            $"{SummarySectionOrder[0]}:\n"
            + TruncateLine(FormattableString.Invariant($"  Incident Id: {incident.Id}"), JiraIssueFormattingLimits.MetadataSectionMaxLength)
            + "\n"
            + TruncateLine(FormattableString.Invariant($"  Fingerprint: {incident.IncidentFingerprint}"), JiraIssueFormattingLimits.MetadataSectionMaxLength)
            + "\n"
            + TruncateLine(FormattableString.Invariant($"  Severity: {incident.Severity}"), JiraIssueFormattingLimits.MetadataSectionMaxLength)
            + "\n"
            + TruncateLine(FormattableString.Invariant($"  Category: {incident.Category}"), JiraIssueFormattingLimits.MetadataSectionMaxLength)
            + "\n"
            + TruncateLine(FormattableString.Invariant($"  Source: {incident.Source}"), JiraIssueFormattingLimits.MetadataSectionMaxLength)
            + "\n"
            + TruncateLine(
                FormattableString.Invariant($"  Occurrence count: {incident.OccurrenceCount}"),
                JiraIssueFormattingLimits.MetadataSectionMaxLength)
            + "\n"
            + TruncateLine(
                FormattableString.Invariant($"  First seen (UTC): {incident.FirstSeenUtc:O}"),
                JiraIssueFormattingLimits.MetadataSectionMaxLength)
            + "\n"
            + TruncateLine(
                FormattableString.Invariant($"  Last seen (UTC): {incident.LastSeenUtc:O}"),
                JiraIssueFormattingLimits.MetadataSectionMaxLength)
            + "\n"
            + TruncateLine(
                FormattableString.Invariant($"  Status: {incident.Status}"),
                JiraIssueFormattingLimits.MetadataSectionMaxLength);

        var blocks = new List<string> { meta };

        if (!string.IsNullOrWhiteSpace(incident.OperationalTitle))
        {
            var titleBlock = TruncateBlock(
                incident.OperationalTitle.Trim(),
                JiraIssueFormattingLimits.TechnicalSummaryMaxLength);
            blocks.Add($"{SummarySectionOrder[1]}:\n{titleBlock}");
        }

        if (!string.IsNullOrWhiteSpace(incident.EvidenceLogExcerpt))
        {
            var evidence = TruncateBlock(
                incident.EvidenceLogExcerpt.Trim(),
                JiraIssueFormattingLimits.EvidenceLogExcerptMaxLength);
            blocks.Add($"{SummarySectionOrder[2]}:\n{evidence}");
        }

        var summaryBody = TruncateBlock(incident.TechnicalSummary ?? string.Empty, JiraIssueFormattingLimits.TechnicalSummaryMaxLength);

        var rootCause = TruncateBlock(incident.PossibleRootCause ?? string.Empty, JiraIssueFormattingLimits.PossibleRootCauseMaxLength);

        var action = TruncateBlock(incident.RecommendedAction ?? string.Empty, JiraIssueFormattingLimits.RecommendedActionMaxLength);

        var audit =
            $"{SummarySectionOrder[6]}:\n"
            + TruncateLine($"  Confidence: {confidenceText}", JiraIssueFormattingLimits.AuditLinesMaxLength)
            + "\n"
            + TruncateLine($"  AiModel: {incident.AiModel}", JiraIssueFormattingLimits.AuditLinesMaxLength)
            + "\n"
            + TruncateLine($"  PromptVersion: {incident.PromptVersion}", JiraIssueFormattingLimits.AuditLinesMaxLength)
            + "\n"
            + TruncateLine($"  PipelineVersion: {incident.PipelineVersion}", JiraIssueFormattingLimits.AuditLinesMaxLength);

        blocks.Add($"{SummarySectionOrder[3]}:\n{summaryBody}");
        blocks.Add($"{SummarySectionOrder[4]}:\n{rootCause}");
        blocks.Add($"{SummarySectionOrder[5]}:\n{action}");
        blocks.Add(audit);

        return string.Join("\n\n", blocks);
    }

    private static string CollapseWhitespace(string value)
    {
        Span<char> buffer = stackalloc char[value.Length];
        var written = 0;
        var previousWasSpace = false;

        foreach (var ch in value)
        {
            var isSpace = ch is '\r' or '\n' or '\t' or ' ';
            var normalized = isSpace ? ' ' : ch;

            if (isSpace)
            {
                if (previousWasSpace)
                {
                    continue;
                }

                previousWasSpace = true;
            }
            else
            {
                previousWasSpace = false;
            }

            buffer[written++] = normalized;
        }

        var slice = buffer[..written];
        while (slice.Length > 0 && slice[0] == ' ')
        {
            slice = slice[1..];
        }

        while (slice.Length > 0 && slice[^1] == ' ')
        {
            slice = slice[..^1];
        }

        return slice.ToString();
    }

    private static string TruncateLine(string line, int maxChars)
    {
        if (line.Length <= maxChars)
        {
            return line;
        }

        var ellipsis = JiraIssueFormattingLimits.SummaryEllipsis;
        var take = Math.Max(0, maxChars - ellipsis.Length);
        return string.Concat(line.AsSpan(0, take), ellipsis);
    }

    private static string TruncateBlock(string text, int maxChars)
    {
        var trimmed = text.Trim();
        if (trimmed.Length <= maxChars)
        {
            return trimmed;
        }

        var ellipsis = JiraIssueFormattingLimits.SummaryEllipsis;
        var take = Math.Max(0, maxChars - ellipsis.Length);
        return string.Concat(trimmed.AsSpan(0, take), ellipsis);
    }
}
