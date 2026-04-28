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
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("AI response did not contain a JSON object.");
        }

        var result = JsonSerializer.Deserialize<LogAnalysisResult>(
            normalized,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (!IsValid(result))
        {
            throw new InvalidOperationException("AI response did not match the expected schema.");
        }

        result!.Severity = result.Severity.Trim().ToLowerInvariant();
        result.Category = result.Category.Trim().ToLowerInvariant();
        return result;
    }

    private static bool IsValid(LogAnalysisResult? result)
    {
        if (result is null)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(result.Severity)
            && !string.IsNullOrWhiteSpace(result.Category)
            && !string.IsNullOrWhiteSpace(result.Summary)
            && !string.IsNullOrWhiteSpace(result.Suggestion)
            && result.Confidence is >= 0 and <= 1;
    }

    private static string ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : string.Empty;
    }
}
