using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using LogAnalyzer.Domain.Interfaces;

namespace LogAnalyzer.Infrastructure.Services;

public partial class SimpleLogGroupingService : ILogGroupingService
{
    public string CreateGroupId(string log)
    {
        var normalized = Normalize(log);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return $"grp-{Convert.ToHexString(bytes)[..12].ToLowerInvariant()}";
    }

    private static string Normalize(string log)
    {
        var normalized = log.ToLowerInvariant();
        normalized = IsoDateRegex().Replace(normalized, " ");
        normalized = TimeRegex().Replace(normalized, " ");
        normalized = GuidRegex().Replace(normalized, " ");
        normalized = DigitRegex().Replace(normalized, "#");
        normalized = WhitespaceRegex().Replace(normalized, " ").Trim();
        return normalized;
    }

    [GeneratedRegex(@"\b\d{4}-\d{2}-\d{2}\b")]
    private static partial Regex IsoDateRegex();

    [GeneratedRegex(@"\b\d{2}:\d{2}:\d{2}(?:\.\d+)?\b")]
    private static partial Regex TimeRegex();

    [GeneratedRegex(@"\b[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}\b", RegexOptions.IgnoreCase)]
    private static partial Regex GuidRegex();

    [GeneratedRegex(@"\d+")]
    private static partial Regex DigitRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
