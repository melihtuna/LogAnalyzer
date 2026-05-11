using LogAnalyzer.Domain.Models;

namespace LogAnalyzer.AI.Classification;

internal static class MinimalSafeClassificationFallback
{
    public static LogAnalysisResult Create(string rawPrimaryAssistantOutput, int classificationRetryCount)
    {
        var r = new LogAnalysisResult
        {
            Severity = "low",
            Category = "unknown",
            Summary = ClassificationContracts.MinimalSafeTexts.TechnicalSummary,
            PossibleRootCause = ClassificationContracts.MinimalSafeTexts.PossibleRootCause,
            Suggestion = ClassificationContracts.MinimalSafeTexts.RecommendedAction,
            Confidence = 0.0,
            RawAIResponse = rawPrimaryAssistantOutput,
            ClassificationSchemaVersion = ClassificationContracts.SchemaVersionV1,
            ClassificationParseMode = ClassificationContracts.ParseModeMinimalSafeFallback,
            ClassificationFallbackUsed = ClassificationContracts.FallbackMinimalSafe,
            ClassificationRetryCount = classificationRetryCount,
            ClassificationEnumFallback = true,
            ClassificationConfidenceParseFailed = false,
            ClassificationConfidenceClamped = false
        };

        ClassificationNormalizer.NormalizeTaxonomy(r);
        ClassificationNormalizer.ClampConfidence(r);
        return r;
    }
}
