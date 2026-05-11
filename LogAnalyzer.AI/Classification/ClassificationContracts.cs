namespace LogAnalyzer.AI.Classification;

internal static class ClassificationContracts
{
    internal static readonly HashSet<string> CanonicalSeverities =
        new(StringComparer.OrdinalIgnoreCase) { "critical", "high", "medium", "low" };

    internal static readonly HashSet<string> CanonicalCategories =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "backend",
            "ui",
            "database",
            "infrastructure",
            "authentication",
            "external_service",
            "network",
            "unknown"
        };

    public const string SchemaVersionV1 = "1";

    public const string ParseModeStrictV1 = "strict_v1";

    public const string ParseModeStrictV1Repaired = "strict_v1_repaired";

    public const string ParseModeLegacyLift = "legacy_lift";

    public const string ParseModeMinimalSafeFallback = "minimal_safe_fallback";

    public const string FallbackFalse = "false";

    public const string FallbackLegacyLift = "legacy_lift";

    public const string FallbackMinimalSafe = "minimal_safe_fallback";

    public static class MinimalSafeTexts
    {
        public const string TechnicalSummary =
            "Automated analysis unavailable; raw logs should be reviewed manually.";

        public const string PossibleRootCause = "Root cause could not be determined automatically.";

        public const string RecommendedAction =
            "Review the attached log evidence and re-run analysis when the AI service is healthy.";
    }

    public const string LegacyLiftRootCausePlaceholder =
        "Root cause not extracted by legacy model output.";
}
