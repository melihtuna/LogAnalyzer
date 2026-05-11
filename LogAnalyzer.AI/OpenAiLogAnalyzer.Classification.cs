using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LogAnalyzer.AI.Classification;
using LogAnalyzer.Domain.Models;
using Microsoft.Extensions.Logging;

namespace LogAnalyzer.AI;

public partial class OpenAiLogAnalyzer
{
    private async Task<string> PostChatCompletionAsync(string userPrompt, CancellationToken cancellationToken)
    {
        var apiKey = configuration["OpenAI:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("OpenAI API key is not configured.");
        }

        var model = ResolveChatModel(configuration);

        using var request = new HttpRequestMessage(HttpMethod.Post, ChatCompletionsEndpoint)
        {
            Content = JsonContent.Create(new
            {
                model,
                messages = new[]
                {
                    new { role = "user", content = userPrompt }
                }
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await SendWithRateLimitRetryAsync(request, cancellationToken).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var (errCode, errMessage, _) = ParseOpenAiError(responseBody);
            logger.LogError(
                "OpenAI HTTP {StatusCode}: code={ErrorCode}, message={ErrorMessage}, body={BodySnippet}",
                (int)response.StatusCode,
                errCode ?? "(none)",
                TruncateForLog(errMessage),
                TruncateForLog(responseBody, 800));
            throw new InvalidOperationException(
                $"OpenAI API failed ({(int)response.StatusCode}). code={errCode}; {errMessage ?? responseBody}");
        }

        using var envelopeDocument = JsonDocument.Parse(responseBody);
        var aiOutput = envelopeDocument.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        if (string.IsNullOrWhiteSpace(aiOutput))
        {
            throw new InvalidOperationException("AI returned an empty response.");
        }

        var responseId = envelopeDocument.RootElement.TryGetProperty("id", out var idProp)
            ? idProp.GetString()
            : null;
        logger.LogInformation(
            "OpenAI response id={ResponseId}, assistant_message_length={Length}, assistant_message={AssistantMessage}",
            responseId,
            aiOutput.Length,
            TruncateForLog(aiOutput, MaxAssistantMessageLogChars));

        return aiOutput;
    }

    private async Task<LogAnalysisResult> ResolveStructuredClassificationAsync(
        string primaryAssistantOutput,
        JsonSerializerOptions jsonOptions,
        CancellationToken cancellationToken)
    {
        LogAnalysisResult FromStrict(
            StructuredClassificationV1Model m,
            string parseMode,
            int retryCount,
            string? repairRaw)
        {
            var r = new LogAnalysisResult
            {
                Severity = m.Severity,
                Category = m.Category,
                Summary = m.TechnicalSummary,
                PossibleRootCause = m.PossibleRootCause,
                Suggestion = m.RecommendedAction,
                Confidence = m.Confidence,
                ClassificationSchemaVersion = ClassificationContracts.SchemaVersionV1,
                ClassificationParseMode = parseMode,
                ClassificationFallbackUsed = ClassificationContracts.FallbackFalse,
                ClassificationRetryCount = retryCount,
                ClassificationRepairRawResponse = repairRaw
            };

            ClassificationNormalizer.NormalizeTaxonomy(r);
            ClassificationNormalizer.ClampConfidence(r);
            return r;
        }

        var primaryJson = ExtractFirstBalancedJsonObject(primaryAssistantOutput);
        if (!string.IsNullOrWhiteSpace(primaryJson)
            && StructuredClassificationV1Parser.TryParseStrictV1(primaryJson, out var strictPrimary))
        {
            logger.LogDebug(
                "Classification transition: path={Path} retry_count={Retries} fallback_used={Fallback}",
                ClassificationContracts.ParseModeStrictV1,
                0,
                ClassificationContracts.FallbackFalse);
            return FromStrict(strictPrimary, ClassificationContracts.ParseModeStrictV1, 0, null);
        }

        logger.LogDebug(
            "Classification transition: primary strict parse failed; invoking repair completion (single retry)");

        var repairPrompt = BuildRepairPrompt(primaryAssistantOutput);
        string repairOutput;
        try
        {
            repairOutput = await PostChatCompletionAsync(repairPrompt, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Classification repair completion failed; falling back to legacy/minimal parsing.");
            logger.LogDebug("Classification transition: repair completion threw; continuing to legacy/minimal path");
            repairOutput = string.Empty;
        }

        var repairJson = ExtractFirstBalancedJsonObject(repairOutput);
        if (!string.IsNullOrWhiteSpace(repairJson)
            && StructuredClassificationV1Parser.TryParseStrictV1(repairJson, out var strictRepair))
        {
            logger.LogDebug(
                "Classification transition: path={Path} retry_count={Retries} fallback_used={Fallback}",
                ClassificationContracts.ParseModeStrictV1Repaired,
                1,
                ClassificationContracts.FallbackFalse);
            return FromStrict(strictRepair, ClassificationContracts.ParseModeStrictV1Repaired, 1, repairOutput);
        }

        logger.LogDebug("Classification transition: repair strict parse failed; attempting legacy lift");

        LogAnalysisResult? legacyAfterRepair = null;
        if (!string.IsNullOrWhiteSpace(repairOutput))
        {
            legacyAfterRepair = TryDeserializeStructuredAnalysis(repairOutput, jsonOptions).Result;
        }

        var legacyPrimary = TryDeserializeStructuredAnalysis(primaryAssistantOutput, jsonOptions).Result;
        var mergedLegacy = legacyAfterRepair ?? legacyPrimary;
        if (mergedLegacy is not null)
        {
            logger.LogDebug(
                "Classification transition: path={Path} retry_count={Retries} fallback_used={Fallback}",
                ClassificationContracts.ParseModeLegacyLift,
                1,
                ClassificationContracts.FallbackLegacyLift);
            mergedLegacy.RawAIResponse = primaryAssistantOutput;
            mergedLegacy.ClassificationRepairRawResponse = legacyAfterRepair is not null ? repairOutput : null;
            mergedLegacy.ClassificationRetryCount = 1;
            LegacyClassificationLift.Apply(mergedLegacy);
            return mergedLegacy;
        }

        logger.LogDebug(
            "Classification transition: path={Path} retry_count={Retries} fallback_used={Fallback}",
            ClassificationContracts.ParseModeMinimalSafeFallback,
            1,
            ClassificationContracts.FallbackMinimalSafe);

        return MinimalSafeClassificationFallback.Create(primaryAssistantOutput, classificationRetryCount: 1);
    }

    private static string BuildRepairPrompt(string invalidAssistantOutput) =>
        """
You failed the structured output contract. Return ONLY one JSON object and nothing else (no markdown fences, no prose).
Allowed root keys exactly: schema_version, severity, category, technical_summary, possible_root_cause, recommended_action, confidence.
schema_version must be "1".
severity must be one of: critical, high, medium, low.
category must be one of: backend, ui, database, infrastructure, authentication, external_service, network, unknown.
technical_summary, possible_root_cause, recommended_action must be non-empty strings.
confidence must be a JSON number between 0 and 1.

Invalid prior output:
"""
        + invalidAssistantOutput;
}
