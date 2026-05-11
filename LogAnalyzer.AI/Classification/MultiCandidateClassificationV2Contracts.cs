using System;
using System.Collections.Generic;
using System.Text.Json;
using LogAnalyzer.Domain.Models;

namespace LogAnalyzer.AI.Classification;

internal static class MultiCandidateClassificationV2Contracts
{
    public const string SchemaVersionV2 = "2";

    public const string ParseModeStrictV2 = "strict_v2";
    public const string ParseModeStrictV2Repaired = "strict_v2_repaired";
    public const string ParseModeLegacyLiftV2 = "legacy_lift_v2";
    public const string ParseModeMinimalSafeFallbackV2 = "minimal_safe_fallback_v2";

    public const string FallbackFalse = "false";
    public const string FallbackLegacyLiftV2 = "legacy_lift_v2";
    public const string FallbackMinimalSafeV2 = "minimal_safe_fallback";

    private static readonly HashSet<string> AllowedRootKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "schema_version",
        "candidates"
    };

    private static readonly HashSet<string> AllowedCandidateKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "severity",
        "category",
        "technical_summary",
        "possible_root_cause",
        "recommended_action",
        "confidence",
        "normalized_operational_title",
        "matching_terms"
    };

    private static bool TryGetRequiredString(
        JsonElement root,
        string name,
        out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(name, out var prop))
        {
            return false;
        }

        if (prop.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = prop.GetString() ?? string.Empty;
        return true;
    }

    public static bool TryParseStrictV2(
        string jsonObject,
        out LogAnalysisBatchCandidatesResult result)
    {
        result = new LogAnalysisBatchCandidatesResult();

        if (string.IsNullOrWhiteSpace(jsonObject))
        {
            return false;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(jsonObject);
        }
        catch (JsonException)
        {
            return false;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var prop in root.EnumerateObject())
            {
                if (!AllowedRootKeys.Contains(prop.Name))
                {
                    return false;
                }
            }

            if (!TryGetRequiredString(root, "schema_version", out var schemaVersion)
                || !string.Equals(schemaVersion.Trim(), SchemaVersionV2, StringComparison.Ordinal))
            {
                return false;
            }

            if (!root.TryGetProperty("candidates", out var candidatesProp)
                || candidatesProp.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            if (candidatesProp.GetArrayLength() is 0 or > 5)
            {
                return false;
            }

            var candidates = new List<OperationalIncidentCandidate>();
            foreach (var candidateEl in candidatesProp.EnumerateArray())
            {
                if (candidateEl.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                foreach (var prop in candidateEl.EnumerateObject())
                {
                    if (!AllowedCandidateKeys.Contains(prop.Name))
                    {
                        return false;
                    }
                }

                if (!TryGetRequiredString(candidateEl, "severity", out var severity)
                    || !TryGetRequiredString(candidateEl, "category", out var category)
                    || !TryGetRequiredString(candidateEl, "technical_summary", out var technicalSummary)
                    || !TryGetRequiredString(candidateEl, "possible_root_cause", out var rootCause)
                    || !TryGetRequiredString(candidateEl, "recommended_action", out var recommendedAction)
                    || !TryGetRequiredString(candidateEl, "normalized_operational_title", out var operationalTitle))
                {
                    return false;
                }

                if (string.IsNullOrWhiteSpace(technicalSummary)
                    || string.IsNullOrWhiteSpace(rootCause)
                    || string.IsNullOrWhiteSpace(recommendedAction)
                    || string.IsNullOrWhiteSpace(operationalTitle))
                {
                    return false;
                }

                if (!candidateEl.TryGetProperty("confidence", out var confEl)
                    || confEl.ValueKind != JsonValueKind.Number
                    || !confEl.TryGetDouble(out var confidence))
                {
                    return false;
                }

                confidence = Math.Clamp(confidence, 0.0, 1.0);

                if (!candidateEl.TryGetProperty("matching_terms", out var termsEl)
                    || termsEl.ValueKind != JsonValueKind.Array)
                {
                    return false;
                }

                var terms = new List<string>();
                foreach (var t in termsEl.EnumerateArray())
                {
                    if (t.ValueKind != JsonValueKind.String)
                    {
                        return false;
                    }

                    var s = (t.GetString() ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(s))
                    {
                        return false;
                    }

                    if (s.Length > 64)
                    {
                        return false;
                    }

                    terms.Add(s);
                }

                if (terms.Count is 0 or > 8)
                {
                    return false;
                }

                var sev = severity.Trim().ToLowerInvariant();
                var cat = category.Trim().ToLowerInvariant();
                if (!ClassificationContracts.CanonicalSeverities.Contains(sev)
                    || !ClassificationContracts.CanonicalCategories.Contains(cat))
                {
                    return false;
                }

                candidates.Add(new OperationalIncidentCandidate
                {
                    Severity = sev,
                    Category = cat,
                    TechnicalSummary = technicalSummary.Trim(),
                    PossibleRootCause = rootCause.Trim(),
                    RecommendedAction = recommendedAction.Trim(),
                    Confidence = confidence,
                    NormalizedOperationalTitle = operationalTitle.Trim(),
                    MatchingTerms = terms
                });
            }

            if (candidates.Count is 0)
            {
                return false;
            }

            result.SchemaVersion = schemaVersion.Trim();
            result.Candidates = candidates;
            return true;
        }
    }
}

