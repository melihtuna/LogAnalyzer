using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LogAnalyzer.AI.Classification;
using LogAnalyzer.Domain.Interfaces;
using LogAnalyzer.Domain.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LogAnalyzer.AI;

public partial class OpenAiLogAnalyzer(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<OpenAiLogAnalyzer> logger) : ILogAnalyzerAI
{
    private const string ChatCompletionsEndpoint = "https://api.openai.com/v1/chat/completions";
    private const string OpenAiOrganizationBillingUrl = "https://platform.openai.com/settings/organization/billing";
    private const int MaxRateLimitRetries = 5;
    private const int MaxAssistantMessageLogChars = 32000;
    private const int DefaultPromptBodyLogChars = 65536;
    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(5);

    public async Task<LogAnalysisResult> AnalyzeAsync(string log, CancellationToken cancellationToken = default)
    {
        var apiKey = configuration["OpenAI:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("OpenAI API key is not configured.");
        }

        var model = ResolveChatModel(configuration);
        log = TruncateLogPayload(configuration, log);

        var prompt =
            """
You are a senior .NET backend engineer. The input may contain multiple log lines or distinct incidents.

The operator already reads raw logs. Your job is a single consolidated assessment: themes across the batch,
overall risk, prioritized remediation, and the most likely root cause—not a per-line transcript.

Return ONLY one JSON object (no markdown fences, no prose before or after, no JSON array).
Allowed root keys exactly:
schema_version, severity, category, technical_summary, possible_root_cause, recommended_action, confidence.

Field rules:
- schema_version must be "1".
- severity must be one of: critical, high, medium, low.
- category must be one of: backend, ui, database, infrastructure, authentication, external_service, network, unknown.
- technical_summary: single narrative integrating dominant failures and blast radius; mention file:line hints when stack traces exist.
- possible_root_cause: best hypothesis; if uncertain, state uncertainty explicitly in one sentence.
- recommended_action: prioritized actionable steps tied to concrete locations when possible.
- confidence: JSON number between 0 and 1.

Logs:
"""
            + log;

        var promptLineCount = prompt.Split(["\r\n", "\n"], StringSplitOptions.None).Length;
        var promptBodyLogMax = ResolvePromptBodyLogMaxChars(configuration);
        logger.LogInformation(
            "OpenAI request: model {Model}, prompt {PromptLineCount} lines, {PromptCharCount} characters.",
            model,
            promptLineCount,
            prompt.Length);
        logger.LogInformation(
            "OpenAI prompt body ({TotalChars} chars; docker log cap {LogCapChars}): {PromptBody}",
            prompt.Length,
            promptBodyLogMax,
            TruncateForLog(prompt, promptBodyLogMax));

        var primaryAssistantOutput = await PostChatCompletionAsync(prompt, cancellationToken).ConfigureAwait(false);

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        var result = await ResolveStructuredClassificationAsync(primaryAssistantOutput, jsonOptions, cancellationToken)
            .ConfigureAwait(false);

        result.RawAIResponse = primaryAssistantOutput;

        using (ClassificationTelemetry.BeginLoggingScope(logger, result))
        {
            ClassificationTelemetry.ApplyActivityTags(result);
            logger.LogInformation(
                "Classification resolved: parse_mode={ParseMode}, fallback_used={Fallback}, retry_count={Retries}, confidence={Confidence}",
                result.ClassificationParseMode,
                result.ClassificationFallbackUsed,
                result.ClassificationRetryCount,
                result.Confidence);
        }

        return result;
    }

    private static string ResolveChatModel(IConfiguration configuration)
    {
        var configured = configuration["OpenAI:Model"]?.Trim();
        return string.IsNullOrEmpty(configured) ? "gpt-4o-mini" : configured;
    }

    /// <summary>
    /// Caps how much of the outbound user prompt is written to logs (e.g. Docker). Full payload is still sent to the API.
    /// </summary>
    private static int ResolvePromptBodyLogMaxChars(IConfiguration configuration)
    {
        if (!int.TryParse(configuration["OpenAI:MaxPromptLogCharacters"], out var configured))
        {
            return DefaultPromptBodyLogChars;
        }

        return Math.Clamp(configured, 512, 250_000);
    }

    private static string TruncateLogPayload(IConfiguration configuration, string log)
    {
        var maxChars = 8000;
        if (int.TryParse(configuration["OpenAI:MaxLogCharacters"], out var configured))
        {
            maxChars = Math.Clamp(configured, 1500, 50000);
        }

        if (string.IsNullOrEmpty(log) || log.Length <= maxChars)
        {
            return log;
        }

        return log[..maxChars] + "\n...[truncated: log payload exceeded OpenAI:MaxLogCharacters]";
    }

    private async Task<HttpResponseMessage> SendWithRateLimitRetryAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxRateLimitRetries; attempt++)
        {
            var retryRequest = CloneRequest(request);
            var response = await httpClient.SendAsync(retryRequest, cancellationToken);
            if ((int)response.StatusCode != 429)
            {
                return response;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var (code, message, _) = ParseOpenAiError(body);

            if (attempt == 1)
            {
                logger.LogWarning(
                    "OpenAI returned HTTP 429. If this persists after retries, add prepaid credits or confirm billing: {BillingUrl}",
                    OpenAiOrganizationBillingUrl);
            }

            if (IsBillingOrQuotaBlock(code))
            {
                logger.LogError(
                    "OpenAI billing or quota block (429, code={ErrorCode}): {Message}. " +
                    "Add credits or a payment method if the balance is depleted. Limits: https://platform.openai.com/settings/organization/limits — Billing: {BillingUrl}",
                    code,
                    TruncateForLog(message ?? body, 500),
                    OpenAiOrganizationBillingUrl);
                response.Dispose();
                throw new InvalidOperationException(
                    $"OpenAI billing or quota: {code}. {message ?? body}");
            }

            if (attempt == MaxRateLimitRetries)
            {
                logger.LogError(
                    "OpenAI 429: still rate-limited after {Max} attempts (code={ErrorCode}, message={Message}). " +
                    "If usage shows zero but calls fail, add credits or check organization billing: {BillingUrl}",
                    MaxRateLimitRetries,
                    code ?? "(none)",
                    TruncateForLog(message ?? body, 500),
                    OpenAiOrganizationBillingUrl);
                response.Dispose();
                throw new InvalidOperationException(
                    $"OpenAI 429 (rate limit). code={code}; {message ?? body}");
            }

            var delay = GetRetryDelay(response, attempt);
            logger.LogWarning(
                "OpenAI 429 (attempt {Attempt}/{Max}): code={ErrorCode}; retrying in {DelaySeconds:F1}s.",
                attempt,
                MaxRateLimitRetries,
                code ?? "(none)",
                delay.TotalSeconds);
            response.Dispose();
            await Task.Delay(delay, cancellationToken);
        }

        throw new InvalidOperationException("Unexpected OpenAI request retry flow.");
    }

    private static bool IsBillingOrQuotaBlock(string? code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return false;
        }

        return string.Equals(code, "insufficient_quota", StringComparison.OrdinalIgnoreCase)
               || string.Equals(code, "billing_hard_limit_reached", StringComparison.OrdinalIgnoreCase);
    }

    private static (string? Code, string? Message, string? Type) ParseOpenAiError(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return (null, null, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("error", out var err))
            {
                return (null, null, null);
            }

            var code = err.TryGetProperty("code", out var c) ? c.GetString() : null;
            var message = err.TryGetProperty("message", out var m) ? m.GetString() : null;
            var type = err.TryGetProperty("type", out var t) ? t.GetString() : null;
            return (code, message, type);
        }
        catch (JsonException)
        {
            return (null, null, null);
        }
    }

    private static string TruncateForLog(string? text, int maxChars = 400)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Length <= maxChars ? text : text[..maxChars] + "...";
    }

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.RetryAfter?.Delta is TimeSpan delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        if (response.Headers.RetryAfter?.Date is DateTimeOffset date)
        {
            var duration = date - DateTimeOffset.UtcNow;
            if (duration > TimeSpan.Zero)
            {
                return duration;
            }
        }

        var seconds = DefaultRetryDelay.TotalSeconds * Math.Pow(2, attempt - 1);
        return TimeSpan.FromSeconds(Math.Min(seconds, 120));
    }

    private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            var contentJson = request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            clone.Content = new StringContent(contentJson, System.Text.Encoding.UTF8, "application/json");
        }

        return clone;
    }

    /// <summary>
    /// Parses a JSON array of results, or multiple top-level objects separated by commas (legacy model output).
    /// Returns merged aggregate plus fragment count for logging.
    /// </summary>
    private static (LogAnalysisResult? Result, int FragmentCount) TryDeserializeStructuredAnalysis(
        string aiOutput,
        JsonSerializerOptions jsonOptions)
    {
        var trimmed = aiOutput.TrimStart();
        if (trimmed.Length > 0 && trimmed[0] == '[')
        {
            var arrayJson = ExtractBalancedJsonBracketSlice(trimmed, 0, '[', ']', out _);
            if (!string.IsNullOrEmpty(arrayJson))
            {
                try
                {
                    var batch = JsonSerializer.Deserialize<List<LogAnalysisResult>>(arrayJson, jsonOptions);
                    if (batch is { Count: > 0 })
                    {
                        return (MergeLogAnalysisBatch(batch), batch.Count);
                    }
                }
                catch (JsonException)
                {
                    // fall through to object fragments
                }
            }
        }

        var objects = ExtractAllBalancedJsonObjects(aiOutput);
        if (objects.Count == 0)
        {
            return (null, 0);
        }

        var parsed = new List<LogAnalysisResult>();
        foreach (var obj in objects)
        {
            try
            {
                var item = JsonSerializer.Deserialize<LogAnalysisResult>(obj, jsonOptions);
                if (item is not null)
                {
                    parsed.Add(item);
                }
            }
            catch (JsonException)
            {
                // skip malformed trailing fragments (e.g. truncated last object)
            }
        }

        if (parsed.Count == 0)
        {
            return (null, 0);
        }

        return (MergeLogAnalysisBatch(parsed), parsed.Count);
    }

    private static LogAnalysisResult MergeLogAnalysisBatch(IReadOnlyList<LogAnalysisResult> items)
    {
        if (items.Count == 0)
        {
            throw new ArgumentException("At least one item is required.", nameof(items));
        }

        if (items.Count == 1)
        {
            var only = items[0];
            var single = new LogAnalysisResult
            {
                Severity = only.Severity,
                Category = only.Category,
                Summary = only.Summary,
                Suggestion = only.Suggestion,
                Confidence = only.Confidence,
                PossibleRootCause = only.PossibleRootCause,
                RawAIResponse = string.Empty
            };
            ClassificationNormalizer.NormalizeTaxonomy(single);
            return single;
        }

        var normalizedItems = new List<LogAnalysisResult>();
        foreach (var r in items)
        {
            var copy = new LogAnalysisResult
            {
                Severity = r.Severity,
                Category = r.Category,
                Summary = r.Summary,
                Suggestion = r.Suggestion,
                Confidence = r.Confidence,
                PossibleRootCause = r.PossibleRootCause,
                RawAIResponse = r.RawAIResponse
            };
            ClassificationNormalizer.NormalizeTaxonomy(copy);
            normalizedItems.Add(copy);
        }

        var worst = normalizedItems.OrderByDescending(r => SeverityRank(r.Severity)).First();
        var categories = normalizedItems.Select(r => r.Category).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var category = categories.Count == 1 ? categories[0] : "unknown";

        var summaries = normalizedItems
            .Select(r => r.Summary.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var summary = string.Join(" | ", summaries);
        const int maxJoined = 1600;
        if (summary.Length > maxJoined)
        {
            summary = summary[..maxJoined] + "...";
        }

        var suggestions = normalizedItems
            .Select(r => r.Suggestion.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var suggestion = suggestions.Count == 0
            ? worst.Suggestion
            : string.Join(" | ", suggestions);
        if (suggestion.Length > maxJoined)
        {
            suggestion = suggestion[..maxJoined] + "...";
        }

        var confidenceSum = normalizedItems.Sum(r => r.Confidence <= 0 ? 0.25 : r.Confidence);
        var confidence = confidenceSum / normalizedItems.Count;

        var rootCauses = normalizedItems
            .Select(r => r.PossibleRootCause.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var mergedRootCause = rootCauses.Count == 0
            ? string.Empty
            : string.Join(" | ", rootCauses);
        if (mergedRootCause.Length > maxJoined)
        {
            mergedRootCause = mergedRootCause[..maxJoined] + "...";
        }

        return new LogAnalysisResult
        {
            Severity = worst.Severity,
            Category = category,
            Summary = summary,
            Suggestion = suggestion,
            PossibleRootCause = mergedRootCause,
            Confidence = confidence,
            RawAIResponse = string.Empty
        };
    }

    private static int SeverityRank(string severity)
    {
        return severity.ToLowerInvariant() switch
        {
            "critical" => 4,
            "high" => 3,
            "medium" => 2,
            _ => 1
        };
    }

    private static string ExtractFirstBalancedJsonObject(string text)
    {
        var start = text.IndexOf('{');
        return start < 0 ? string.Empty : ExtractBalancedJsonBracketSlice(text, start, '{', '}', out _);
    }

    private static List<string> ExtractAllBalancedJsonObjects(string text)
    {
        var list = new List<string>();
        var idx = 0;
        while (idx < text.Length)
        {
            var start = text.IndexOf('{', idx);
            if (start < 0)
            {
                break;
            }

            var json = ExtractBalancedJsonBracketSlice(text, start, '{', '}', out var endExclusive);
            if (string.IsNullOrEmpty(json))
            {
                break;
            }

            list.Add(json);
            idx = endExclusive;
            while (idx < text.Length && char.IsWhiteSpace(text[idx]))
            {
                idx++;
            }

            if (idx < text.Length && text[idx] == ',')
            {
                idx++;
            }
        }

        return list;
    }

    /// <summary>
    /// Extracts a balanced slice starting at <paramref name="start"/> where <paramref name="open"/> must match first character.
    /// Respects double-quoted strings and escape sequences.
    /// </summary>
    private static string ExtractBalancedJsonBracketSlice(
        string text,
        int start,
        char open,
        char close,
        out int endExclusive)
    {
        endExclusive = start;
        if (start < 0 || start >= text.Length || text[start] != open)
        {
            return string.Empty;
        }

        var depth = 0;
        var inString = false;
        var escape = false;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (escape)
                {
                    escape = false;
                }
                else if (c == '\\')
                {
                    escape = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c == open)
            {
                depth++;
            }
            else if (c == close)
            {
                depth--;
                if (depth == 0)
                {
                    endExclusive = i + 1;
                    return text[start..endExclusive];
                }
            }
        }

        return string.Empty;
    }
}
