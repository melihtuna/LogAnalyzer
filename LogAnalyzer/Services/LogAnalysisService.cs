namespace LogAnalyzer.Services
{
    using System.Collections.Concurrent;
    using System.Security.Cryptography;
    using System.Text;
    using LogAnalyzer.AI;
    using LogAnalyzer.Models;
    using LogAnalyzer.Tools;

    public class LogAnalysisService
    {
        private static readonly ConcurrentDictionary<string, LogResponse> Cache = new();
        private readonly AiClient _aiClient;
        private readonly LogParser _logParser;

        public LogAnalysisService(AiClient aiClient, LogParser logParser)
        {
            _aiClient = aiClient;
            _logParser = logParser;
        }

        public async Task<LogResponse> AnalyzeAsync(LogRequest request, CancellationToken cancellationToken = default)
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Logs))
            {
                throw new ArgumentException("Logs are required.", nameof(request));
            }

            var cacheKey = ComputeHash(request.Logs);
            if (Cache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            var processedLogs = _logParser.ExtractErrorLinesOrFullLogs(request.Logs);
            var result = await _aiClient.AnalyzeAsync(processedLogs, cancellationToken);
            Cache[cacheKey] = result;
            return result;
        }

        private static string ComputeHash(string input)
        {
            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash);
        }
    }
}
