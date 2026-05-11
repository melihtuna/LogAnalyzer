using System.Text.Json;

namespace LogAnalyzer.AI.Classification;

internal static class StructuredClassificationV1Parser
{
    private static readonly HashSet<string> AllowedRootKeys =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "schema_version",
            "severity",
            "category",
            "technical_summary",
            "possible_root_cause",
            "recommended_action",
            "confidence"
        };

    /// <summary>Parses one JSON object payload (no surrounding prose).</summary>
    public static bool TryParseStrictV1(string jsonObject, out StructuredClassificationV1Model model)
    {
        model = default!;
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
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (!AllowedRootKeys.Contains(prop.Name))
                {
                    return false;
                }
            }

            if (!TryGetRequiredString(doc.RootElement, "schema_version", out var schemaVersion)
                || schemaVersion != ClassificationContracts.SchemaVersionV1)
            {
                return false;
            }

            if (!TryGetRequiredString(doc.RootElement, "severity", out var severity)
                || !TryGetRequiredString(doc.RootElement, "category", out var category)
                || !TryGetRequiredString(doc.RootElement, "technical_summary", out var technicalSummary)
                || !TryGetRequiredString(doc.RootElement, "possible_root_cause", out var rootCause)
                || !TryGetRequiredString(doc.RootElement, "recommended_action", out var recommendedAction))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(technicalSummary)
                || string.IsNullOrWhiteSpace(recommendedAction))
            {
                return false;
            }

            if (!doc.RootElement.TryGetProperty("confidence", out var confProp)
                || confProp.ValueKind != JsonValueKind.Number
                || !confProp.TryGetDouble(out var confidence))
            {
                return false;
            }

            var sev = severity.Trim();
            var cat = category.Trim();
            if (!ClassificationContracts.CanonicalSeverities.Contains(sev)
                || !ClassificationContracts.CanonicalCategories.Contains(cat))
            {
                return false;
            }

            model = new StructuredClassificationV1Model(
                sev.ToLowerInvariant(),
                cat.ToLowerInvariant(),
                technicalSummary.Trim(),
                rootCause.Trim(),
                recommendedAction.Trim(),
                confidence);

            return true;
        }
    }

    private static bool TryGetRequiredString(JsonElement root, string name, out string value)
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
}

internal readonly record struct StructuredClassificationV1Model(
    string Severity,
    string Category,
    string TechnicalSummary,
    string PossibleRootCause,
    string RecommendedAction,
    double Confidence);
