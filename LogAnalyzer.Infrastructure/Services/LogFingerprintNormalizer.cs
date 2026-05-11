using System.Text.RegularExpressions;

namespace LogAnalyzer.Infrastructure.Services;

/// <summary>
/// Normalizes volatile segments so semantically equivalent log lines share the same stable fingerprint.
/// </summary>
public static partial class LogFingerprintNormalizer
{
    public static string NormalizeForStableFingerprint(string log)
    {
        if (string.IsNullOrWhiteSpace(log))
        {
            return string.Empty;
        }

        var lines = log.Split(["\r\n", "\n"], StringSplitOptions.None);
        var normalizedLines = new List<string>(lines.Length);
        foreach (var line in lines)
        {
            var n = NormalizeSingleLine(line);
            if (!string.IsNullOrWhiteSpace(n))
            {
                normalizedLines.Add(n);
            }
        }

        return string.Join('\n', normalizedLines);
    }

    private static string NormalizeSingleLine(string line)
    {
        var s = line.Trim();
        if (s.Length == 0)
        {
            return string.Empty;
        }

        s = GraylogLinePrefixRegex().Replace(s, string.Empty);

        const int maxLeadingTimestampStrips = 4;
        for (var i = 0; i < maxLeadingTimestampStrips && LeadingIso8601Regex().IsMatch(s); i++)
        {
            s = LeadingIso8601Regex().Replace(s, string.Empty, 1);
        }

        s = VolatileCorrelationRegex().Replace(s, "$1=*");
        s = CollapseWhitespaceRegex().Replace(s, " ").Trim();
        return s;
    }

    /// <summary>Leading bracket groups from <see cref="GraylogLogProvider"/> (<c>[level] </c>) or legacy <c>[timestamp] [level] </c>.</summary>
    [GeneratedRegex(@"^(?:\[[^\]]+\]\s+)+")]
    private static partial Regex GraylogLinePrefixRegex();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})\s+")]
    private static partial Regex LeadingIso8601Regex();

    [GeneratedRegex(@"\b(trace_id|span_id|request_id)=\S+", RegexOptions.IgnoreCase)]
    private static partial Regex VolatileCorrelationRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex CollapseWhitespaceRegex();
}
