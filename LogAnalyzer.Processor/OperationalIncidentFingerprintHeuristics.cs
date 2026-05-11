using System.Text.RegularExpressions;
using LogAnalyzer.Domain.Interfaces;
using LogAnalyzer.Infrastructure.Services;

namespace LogAnalyzer.Processor;

internal static class OperationalIncidentFingerprintHeuristics
{
    private static readonly Regex ServiceBracketRegex = new(@"\[([^\]]+)\]", RegexOptions.Compiled);
    private static readonly Regex PostgresSqlStateRegex = new(@"\b40P01\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TlsExpiryRegex = new(@"tls_certificate_expiry_soon|certificate_.*expiry|certificate.*expir", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TimeoutRegex = new(@"timeout|timed out|TimeoutException|SocketException\s*\(110\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ValidationFailedRegex = new(@"validation_failed|FIELD_INVALID|validation failed", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CnRegex = new(@"cn=([^\s,]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PathFileRegex = new(@"in\s+/src/[^/\s]*/(?<file>[^/\s:]+)\.cs", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AtTypeRegex = new(@"\bat\s+(?<type>[A-Za-z0-9_\.]+)\.", RegexOptions.Compiled);

    public static string ComputeGroupIdFromEvidenceOnly(string processedEvidenceLog, ILogGroupingService groupingService)
    {
        var evidence = processedEvidenceLog ?? string.Empty;
        var evidenceLower = evidence.ToLowerInvariant();

        var service = ExtractService(evidence);
        var component = ExtractComponent(evidence);

        var issueType = DetectIssueType(evidenceLower);
        var sqlStateHint = issueType == "deadlock" ? DetectPostgresSqlStateHint(evidence) : null;
        var cn = issueType.StartsWith("tls", StringComparison.OrdinalIgnoreCase) ? DetectCn(evidence) : null;

        var canonical =
            $"type={issueType}|svc={service}|component={component}|sql_state={sqlStateHint}|cn={cn}|scope=operational_group";

        var stableNormalized = LogFingerprintNormalizer.NormalizeForStableFingerprint(canonical);
        return groupingService.CreateGroupId(stableNormalized);
    }

    private static string DetectIssueType(string evidenceLower)
    {
        if (ValidationFailedRegex.IsMatch(evidenceLower))
        {
            return "validation_failure";
        }

        if (PostgresSqlStateRegex.IsMatch(evidenceLower))
        {
            return "deadlock";
        }

        if (evidenceLower.Contains("deadlock", StringComparison.OrdinalIgnoreCase))
        {
            return "deadlock";
        }

        if (TlsExpiryRegex.IsMatch(evidenceLower))
        {
            return "tls_certificate_expiration_warning";
        }

        if (TimeoutRegex.IsMatch(evidenceLower))
        {
            return "timeout";
        }

        return "unknown_operational_problem";
    }

    private static string? DetectPostgresSqlStateHint(string evidence)
    {
        return PostgresSqlStateRegex.IsMatch(evidence) ? "40P01" : null;
    }

    private static string? DetectCn(string evidence)
    {
        var m = CnRegex.Match(evidence);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string ExtractService(string evidence)
    {
        foreach (Match m in ServiceBracketRegex.Matches(evidence))
        {
            var value = (m.Groups[1].Value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (value.All(char.IsDigit))
            {
                continue;
            }

            if (value.EndsWith("Service", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Bff", StringComparison.OrdinalIgnoreCase)
                || value.Contains("ApiGateway", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Controller", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }

            return value;
        }

        var file = ExtractFileName(evidence);
        return string.IsNullOrWhiteSpace(file) ? "unknown_service" : file;
    }

    private static string ExtractComponent(string evidence)
    {
        var file = ExtractFileName(evidence);
        if (!string.IsNullOrWhiteSpace(file))
        {
            return file;
        }

        var type = AtTypeRegex.Match(evidence).Groups["type"].Value;
        if (!string.IsNullOrWhiteSpace(type))
        {
            var lastDot = type.LastIndexOf('.');
            return lastDot >= 0 ? type[(lastDot + 1)..] : type;
        }

        return "unknown_component";
    }

    private static string ExtractFileName(string evidence)
    {
        var m = PathFileRegex.Match(evidence);
        if (!m.Success)
        {
            return string.Empty;
        }

        var file = m.Groups["file"].Value;
        return string.IsNullOrWhiteSpace(file) ? string.Empty : file;
    }
}

