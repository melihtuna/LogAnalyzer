using System.Net.Http.Json;
using System.Text.Json;
using LogAnalyzer.AI.Options;
using LogAnalyzer.Domain.Interfaces;
using LogAnalyzer.Domain.Models;
using Microsoft.Extensions.Options;

namespace LogAnalyzer.AI;

public class OllamaLogAnalyzerAI(HttpClient httpClient, IOptions<OllamaOptions> options) : ILogAnalyzerAI
{
    public async Task<LogAnalysisResult> AnalyzeAsync(string log, CancellationToken cancellationToken = default)
    {
        var prompt =
            """
You are a senior .NET backend engineer analyzing application logs.

Return only valid JSON that matches this schema exactly:
{
  "severity": "critical|high|medium|low",
  "category": "database|auth|network|timeout|application|unknown",
  "summary": "short technical summary",
  "suggestion": "clear next action",
  "confidence": 0.0
}

Rules:
- Use lowercase for severity and category.
- Confidence must be a number between 0 and 1.
- Do not add markdown, comments, or extra text.
- If you are not fully certain, still provide your best possible classification and explanation.
- Prefer a useful partial analysis over refusing to answer.

Logs:
"""
            + log;

        var payload = new
        {
            model = options.Value.Model,
            prompt,
            stream = false
        };

        using var response = await httpClient.PostAsJsonAsync(options.Value.Endpoint, payload, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        using var envelopeDocument = JsonDocument.Parse(responseBody);

        if (!envelopeDocument.RootElement.TryGetProperty("response", out var aiOutputElement))
        {
            throw new InvalidOperationException("AI response did not contain expected content.");
        }

        var aiOutput = aiOutputElement.GetString();
        if (string.IsNullOrWhiteSpace(aiOutput))
        {
            throw new InvalidOperationException("AI returned an empty response.");
        }

        var normalized = ExtractJsonObject(aiOutput);
        LogAnalysisResult? result = null;
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            result = JsonSerializer.Deserialize<LogAnalysisResult>(
                normalized,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        result ??= BuildBestEffortResult(aiOutput);

        result.Severity = NormalizeSeverity(result.Severity);
        result.Category = NormalizeCategory(result.Category);
        result.Summary = string.IsNullOrWhiteSpace(result.Summary)
            ? "AI provided an unstructured analysis output."
            : result.Summary.Trim();
        result.Suggestion = string.IsNullOrWhiteSpace(result.Suggestion)
            ? "Review the related service/component and validate error handling around this path."
            : result.Suggestion.Trim();
        result.Confidence = Math.Clamp(result.Confidence <= 0 ? 0.25 : result.Confidence, 0.05, 1.0);
        result.RawAIResponse = aiOutput;
        return result;
    }

    private static LogAnalysisResult BuildBestEffortResult(string aiOutput)
    {
        return new LogAnalysisResult
        {
            Severity = InferSeverity(aiOutput),
            Category = InferCategory(aiOutput),
            Summary = aiOutput.Length > 400 ? aiOutput[..400] : aiOutput,
            Suggestion = "Use this AI output as best-effort guidance and verify with application telemetry.",
            Confidence = 0.25
        };
    }

    private static string ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : string.Empty;
    }

    private static string NormalizeSeverity(string severity)
    {
        if (string.IsNullOrWhiteSpace(severity))
        {
            return "medium";
        }

        var normalized = severity.Trim().ToLowerInvariant();
        return normalized is "critical" or "high" or "medium" or "low" ? normalized : "medium";
    }

    private static string NormalizeCategory(string category)
    {
        return string.IsNullOrWhiteSpace(category) ? "unknown" : category.Trim().ToLowerInvariant();
    }

    private static string InferSeverity(string text)
    {
        var lowered = text.ToLowerInvariant();
        if (lowered.Contains("critical", StringComparison.Ordinal))
        {
            return "critical";
        }

        if (lowered.Contains("high", StringComparison.Ordinal) || lowered.Contains("exception", StringComparison.Ordinal))
        {
            return "high";
        }

        if (lowered.Contains("low", StringComparison.Ordinal))
        {
            return "low";
        }

        return "medium";
    }

    private static string InferCategory(string text)
    {
        var lowered = text.ToLowerInvariant();
        if (lowered.Contains("auth", StringComparison.Ordinal) || lowered.Contains("token", StringComparison.Ordinal))
        {
            return "auth";
        }

        if (lowered.Contains("database", StringComparison.Ordinal) || lowered.Contains("sql", StringComparison.Ordinal))
        {
            return "database";
        }

        if (lowered.Contains("timeout", StringComparison.Ordinal))
        {
            return "timeout";
        }

        if (lowered.Contains("network", StringComparison.Ordinal) || lowered.Contains("http", StringComparison.Ordinal))
        {
            return "network";
        }

        return "application";
    }
}
