using LogAnalyzer.Domain.Models;

namespace LogAnalyzer.AI.Classification;

internal static class ClassificationNormalizer
{
    private static readonly Dictionary<string, string> CategorySynonyms =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["application"] = "backend",
            ["auth"] = "authentication",
            ["external"] = "external_service",
            ["external service"] = "external_service",
            ["externalservice"] = "external_service",
            ["timeout"] = "infrastructure",
            ["sql"] = "database",
            ["db"] = "database"
        };

    /// <summary>
    /// Maps severity/category to canonical v1 literals; sets <see cref="LogAnalysisResult.ClassificationEnumFallback"/> when a synonym or unknown was corrected.
    /// </summary>
    public static void NormalizeTaxonomy(LogAnalysisResult target)
    {
        var enumFallback = target.ClassificationEnumFallback;

        var rawSev = target.Severity?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(rawSev))
        {
            target.Severity = "low";
            enumFallback = true;
        }
        else if (ClassificationContracts.CanonicalSeverities.Contains(rawSev))
        {
            target.Severity = rawSev.ToLowerInvariant();
        }
        else
        {
            target.Severity = "low";
            enumFallback = true;
        }

        var rawCat = target.Category?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(rawCat))
        {
            target.Category = "unknown";
            enumFallback = true;
        }
        else if (ClassificationContracts.CanonicalCategories.Contains(rawCat))
        {
            target.Category = rawCat.ToLowerInvariant();
        }
        else if (CategorySynonyms.TryGetValue(rawCat, out var mapped))
        {
            target.Category = mapped;
            enumFallback = true;
        }
        else
        {
            target.Category = "unknown";
            enumFallback = true;
        }

        target.ClassificationEnumFallback = enumFallback;
    }

    public static void ClampConfidence(LogAnalysisResult target)
    {
        if (double.IsNaN(target.Confidence) || double.IsInfinity(target.Confidence))
        {
            target.ClassificationConfidenceParseFailed = true;
            target.Confidence = 0.0;
            return;
        }

        var before = target.Confidence;
        var clamped = Math.Clamp(before, 0.0, 1.0);
        if (Math.Abs(clamped - before) > double.Epsilon)
        {
            target.ClassificationConfidenceClamped = true;
        }

        target.Confidence = clamped;
    }
}
