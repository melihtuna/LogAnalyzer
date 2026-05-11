using System.Diagnostics;
using LogAnalyzer.Domain.Models;
using LogAnalyzer.Domain.Observability;
using Microsoft.Extensions.Logging;

namespace LogAnalyzer.AI.Classification;

internal static class ClassificationTelemetry
{
    public static void ApplyActivityTags(LogAnalysisResult result)
    {
        var activity = Activity.Current;
        if (activity is null)
        {
            return;
        }

        activity.SetTag(ObservabilityAttributeKeys.ClassificationSchemaVersion, result.ClassificationSchemaVersion);
        activity.SetTag(ObservabilityAttributeKeys.ClassificationParseMode, result.ClassificationParseMode);
        activity.SetTag(ObservabilityAttributeKeys.ClassificationFallbackUsed, result.ClassificationFallbackUsed);
        activity.SetTag(ObservabilityAttributeKeys.ClassificationRetryCount, result.ClassificationRetryCount);
        activity.SetTag(ObservabilityAttributeKeys.ClassificationConfidence, result.Confidence);

        if (result.ClassificationConfidenceParseFailed)
        {
            activity.SetTag(ObservabilityAttributeKeys.ClassificationConfidenceParseFailed, true);
        }

        if (result.ClassificationConfidenceClamped)
        {
            activity.SetTag(ObservabilityAttributeKeys.ClassificationConfidenceClamped, true);
        }

        if (result.ClassificationEnumFallback)
        {
            activity.SetTag(ObservabilityAttributeKeys.ClassificationEnumFallback, true);
        }
    }

    public static IDisposable BeginLoggingScope(ILogger logger, LogAnalysisResult result)
    {
        return logger.BeginScope(new Dictionary<string, object?>
        {
            [ObservabilityAttributeKeys.ClassificationSchemaVersion] = result.ClassificationSchemaVersion,
            [ObservabilityAttributeKeys.ClassificationParseMode] = result.ClassificationParseMode,
            [ObservabilityAttributeKeys.ClassificationFallbackUsed] = result.ClassificationFallbackUsed,
            [ObservabilityAttributeKeys.ClassificationRetryCount] = result.ClassificationRetryCount,
            [ObservabilityAttributeKeys.ClassificationConfidence] = result.Confidence,
            [ObservabilityAttributeKeys.ClassificationConfidenceParseFailed] = result.ClassificationConfidenceParseFailed
                ? true
                : null,
            [ObservabilityAttributeKeys.ClassificationConfidenceClamped] = result.ClassificationConfidenceClamped
                ? true
                : null,
            [ObservabilityAttributeKeys.ClassificationEnumFallback] = result.ClassificationEnumFallback ? true : null
        })!;
    }
}
