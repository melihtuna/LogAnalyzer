namespace LogAnalyzer.AI
{
    using System.Net.Http.Json;
    using System.Text.Json;
    using LogAnalyzer.Models;

    public class AiClient
    {
        private readonly HttpClient _httpClient;

        public AiClient(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<LogResponse> AnalyzeAsync(string processedLogs, CancellationToken cancellationToken = default)
        {
            var prompt = $@"You are a senior backend engineer.

Analyze the logs and return:
- summary
- root cause
- severity (Low, Medium, High)
- suggestion

Be concise and technical.
Return only valid JSON with this exact shape:
{{
  ""summary"": ""..."",
  ""rootCause"": ""..."",
  ""severity"": ""..."",
  ""suggestion"": ""...""
}}

Logs:
{processedLogs}";

            var payload = new
            {
                model = "llama3",
                prompt,
                stream = false
            };

            using var response = await _httpClient.PostAsJsonAsync("http://localhost:11434/api/generate", payload, cancellationToken);
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

            var parsed = TryParseJson(aiOutput);
            if (parsed is not null)
            {
                return parsed;
            }

            return new LogResponse
            {
                Summary = aiOutput,
                RootCause = "Unable to parse structured root cause from AI output.",
                Severity = "Medium",
                Suggestion = "Refine the prompt or enforce stricter JSON formatting."
            };
        }

        private static LogResponse? TryParseJson(string aiOutput)
        {
            var normalized = ExtractJsonObject(aiOutput);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            try
            {
                using var json = JsonDocument.Parse(normalized);
                var root = json.RootElement;

                return new LogResponse
                {
                    Summary = GetPropertyValue(root, "summary"),
                    RootCause = GetPropertyValue(root, "rootCause"),
                    Severity = GetPropertyValue(root, "severity"),
                    Suggestion = GetPropertyValue(root, "suggestion")
                };
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string GetPropertyValue(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }

        private static string ExtractJsonObject(string text)
        {
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            if (start < 0 || end <= start)
            {
                return string.Empty;
            }

            return text[start..(end + 1)];
        }
    }
}
