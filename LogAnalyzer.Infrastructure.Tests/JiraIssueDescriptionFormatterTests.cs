using LogAnalyzer.Domain.Models;
using LogAnalyzer.Infrastructure.Jira;

namespace LogAnalyzer.Infrastructure.Tests;

public sealed class JiraIssueDescriptionFormatterTests
{
    private readonly JiraIssueDescriptionFormatter _formatter = new();

    [Fact]
    public void Format_preserves_fixed_section_order_in_plain_description()
    {
        var incident = IncidentTestFactory.CreateMinimal(i =>
        {
            i.OperationalTitle = "Inventory API timeout";
            i.EvidenceLogExcerpt = "ERR inventory timeout after 30s";
        });
        var description = JiraIssueDescriptionFormatter.BuildPlainDescription(incident);

        var headings = new[]
        {
            $"{JiraIssueDescriptionFormatter.SummarySectionOrder[0]}:",
            $"{JiraIssueDescriptionFormatter.SummarySectionOrder[1]}:",
            $"{JiraIssueDescriptionFormatter.SummarySectionOrder[2]}:",
            $"{JiraIssueDescriptionFormatter.SummarySectionOrder[3]}:",
            $"{JiraIssueDescriptionFormatter.SummarySectionOrder[4]}:",
            $"{JiraIssueDescriptionFormatter.SummarySectionOrder[5]}:",
            $"{JiraIssueDescriptionFormatter.SummarySectionOrder[6]}:",
        };

        var indices = headings.Select(h => description.IndexOf(h, StringComparison.Ordinal)).ToArray();
        Assert.All(indices, i => Assert.True(i >= 0));
        for (var i = 1; i < indices.Length; i++)
        {
            Assert.True(indices[i] > indices[i - 1]);
        }
    }

    [Fact]
    public void NormalizeSummary_collapses_whitespace_and_trims_for_single_line_title()
    {
        var incident = IncidentTestFactory.CreateMinimal(i =>
            i.TechnicalSummary = "  hello\r\nworld\t foo   bar  ");

        var summary = JiraIssueDescriptionFormatter.NormalizeSummary(incident);
        Assert.Equal("hello world foo bar", summary);
    }

    [Fact]
    public void NormalizeSummary_prefers_operational_title_when_set()
    {
        var incident = IncidentTestFactory.CreateMinimal(i =>
        {
            i.OperationalTitle = "  Payment capture declined  ";
            i.TechnicalSummary = "Long merged narrative about many systems.";
        });

        var summary = JiraIssueDescriptionFormatter.NormalizeSummary(incident);
        Assert.Equal("Payment capture declined", summary);
    }

    [Fact]
    public void NormalizeSummary_truncates_with_ascii_ellipsis()
    {
        var incident = IncidentTestFactory.CreateMinimal(i =>
            i.TechnicalSummary = new string('x', JiraIssueFormattingLimits.SummaryMaxLength + 40));

        var summary = JiraIssueDescriptionFormatter.NormalizeSummary(incident);
        Assert.Equal(JiraIssueFormattingLimits.SummaryMaxLength, summary.Length);
        Assert.EndsWith(JiraIssueFormattingLimits.SummaryEllipsis, summary);
    }

    [Fact]
    public void Format_command_matches_formatter_projection()
    {
        var incident = IncidentTestFactory.CreateMinimal(i => i.Id = 7);
        var cmd = _formatter.Format(incident);

        Assert.Equal(7, cmd.IncidentId);
        Assert.Equal(JiraIssueDescriptionFormatter.NormalizeSummary(incident), cmd.Summary);
        Assert.Equal(JiraIssueDescriptionFormatter.BuildPlainDescription(incident), cmd.Description);
    }

    [Fact]
    public void Confidence_audit_line_uses_invariant_numeric_format()
    {
        var incident = IncidentTestFactory.CreateMinimal(i => i.Confidence = 1.2); // formatter prints as normalized string
        var description = JiraIssueDescriptionFormatter.BuildPlainDescription(incident);
        Assert.Contains("Confidence: 1.2", description);
    }
}
