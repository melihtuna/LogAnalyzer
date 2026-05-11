using LogAnalyzer.Domain.Models;

namespace LogAnalyzer.AI.Classification;

internal static class LegacyClassificationLift
{
    public static void Apply(LogAnalysisResult target)
    {
        if (string.IsNullOrWhiteSpace(target.PossibleRootCause))
        {
            target.PossibleRootCause = ClassificationContracts.LegacyLiftRootCausePlaceholder;
        }

        target.ClassificationSchemaVersion = ClassificationContracts.SchemaVersionV1;
        target.ClassificationParseMode = ClassificationContracts.ParseModeLegacyLift;
        target.ClassificationFallbackUsed = ClassificationContracts.FallbackLegacyLift;

        ClassificationNormalizer.NormalizeTaxonomy(target);
        ApplyLegacyConfidenceSemantics(target);
        ClassificationNormalizer.ClampConfidence(target);
    }

    private static void ApplyLegacyConfidenceSemantics(LogAnalysisResult target)
    {
        // Missing/zero confidence in legacy payloads is treated as unparsed → low confidence semantics.
        if (target.Confidence <= 0 || double.IsNaN(target.Confidence))
        {
            target.ClassificationConfidenceParseFailed = true;
            target.Confidence = 0.0;
        }
    }
}
