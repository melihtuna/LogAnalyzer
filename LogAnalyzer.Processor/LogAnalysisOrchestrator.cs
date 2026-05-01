using LogAnalyzer.Domain.Interfaces;
using LogAnalyzer.Domain.Models;
using Microsoft.Extensions.Logging;

namespace LogAnalyzer.Processor;

public class LogAnalysisOrchestrator(
    ILogAnalysisRepository repository,
    ILogFingerprintService fingerprintService,
    ILogParser logParser,
    ILogAnalyzerAI logAnalyzerAi,
    ILogGroupingService groupingService,
    INotificationService notificationService,
    ILogger<LogAnalysisOrchestrator> logger) : ILogAnalysisOrchestrator
{
    private static readonly TimeSpan AiTimeout = TimeSpan.FromSeconds(8);
    private const double AcceptedConfidenceThreshold = 0.3;

    public async Task<LogAnalysisResponse> AnalyzeAsync(ILogProvider logProvider, bool includeRawAIResponse, CancellationToken cancellationToken = default)
    {
        if (logProvider is null)
        {
            throw new ArgumentNullException(nameof(logProvider));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var logs = await logProvider.GetLogsAsync();
        cancellationToken.ThrowIfCancellationRequested();

        return await AnalyzeInternalAsync(logs, includeRawAIResponse, cancellationToken);
    }

    public async Task<LogAnalysisResponse> AnalyzeAsync(LogRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Logs))
        {
            throw new ArgumentException("Logs payload is required.", nameof(request));
        }

        return await AnalyzeInternalAsync(request.Logs, request.IncludeRawAIResponse, cancellationToken);
    }

    private async Task<LogAnalysisResponse> AnalyzeInternalAsync(string logs, bool includeRawAIResponse, CancellationToken cancellationToken)
    {
        var logEntries = SplitLogEntries(logs);
        var responses = new List<LogAnalysisResponse>(logEntries.Count);
        var allCached = true;

        foreach (var logEntry in logEntries)
        {
            var logHash = fingerprintService.ComputeHash(logEntry);
            var existingRecord = await repository.GetByHashAsync(logHash, cancellationToken);

            if (existingRecord is not null)
            {
                existingRecord.Count += 1;
                existingRecord.LastSeenUtc = DateTime.UtcNow;
                repository.Update(existingRecord);
                responses.Add(Map(existingRecord, isCached: true));
                continue;
            }

            allCached = false;
            var processedLog = logParser.ExtractErrorLinesOrFullLogs(logEntry);
            var groupId = groupingService.CreateGroupId(processedLog);
            var analysis = await AnalyzeWithRetryAsync(processedLog, cancellationToken);
            var aiResult = analysis.Result;

            var record = new LogAnalysisRecord
            {
                LogHash = logHash,
                GroupId = groupId,
                OriginalLog = logEntry,
                ProcessedLog = processedLog,
                Severity = aiResult.Severity,
                Category = aiResult.Category,
                Summary = aiResult.Summary,
                Suggestion = aiResult.Suggestion,
                Confidence = aiResult.Confidence,
                CreatedUtc = DateTime.UtcNow,
                LastSeenUtc = DateTime.UtcNow,
                Count = 1
            };

            await repository.AddAsync(record, cancellationToken);
            responses.Add(Map(record, isCached: false, isLowConfidence: analysis.IsLowConfidence, rawAiResponse: includeRawAIResponse ? aiResult.RawAIResponse : null));

            if (string.Equals(record.Severity, "critical", StringComparison.OrdinalIgnoreCase))
            {
                _ = notificationService.NotifyCriticalAsync(record, CancellationToken.None);
            }
        }

        await repository.SaveChangesAsync(cancellationToken);
        return AggregateResponses(responses, allCached);
    }

    private static LogAnalysisResponse Map(LogAnalysisRecord record, bool isCached, bool isLowConfidence = false, string? rawAiResponse = null)
    {
        return new LogAnalysisResponse
        {
            Severity = record.Severity,
            Category = record.Category,
            Summary = record.Summary,
            Suggestion = record.Suggestion,
            Confidence = record.Confidence,
            GroupId = record.GroupId,
            IsCached = isCached,
            IsLowConfidence = isLowConfidence,
            RawAIResponse = rawAiResponse
        };
    }

    private static LogAnalysisResult CreateFallbackResult(string log, Exception exception)
    {
        var lowered = log.ToLowerInvariant();
        var severity = "low";
        var category = "application";
        var suggestion = "Review the log details and retry the AI analysis when the model is available.";

        if (exception is TimeoutException || lowered.Contains("timeout", StringComparison.Ordinal))
        {
            severity = "medium";
            category = "timeout";
            suggestion = "Investigate latency, retry policies, and external dependencies involved in the timeout.";
        }

        if (lowered.Contains("nullreference", StringComparison.Ordinal) || lowered.Contains("null reference", StringComparison.Ordinal))
        {
            severity = "high";
            category = "application";
            suggestion = "Inspect null guards and object initialization paths around the failing code path.";
        }

        if (lowered.Contains("exception", StringComparison.Ordinal))
        {
            severity = "high";
        }

        return new LogAnalysisResult
        {
            Severity = severity,
            Category = category,
            Summary = "AI analysis failed, so a rule-based fallback classified the log.",
            Suggestion = suggestion,
            Confidence = 0.35
        };
    }

    private async Task<AnalysisOutcome> AnalyzeWithRetryAsync(string processedLog, CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        LogAnalysisResult? lowConfidenceResult = null;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var timeoutCts = new CancellationTokenSource(AiTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                var result = await logAnalyzerAi.AnalyzeAsync(processedLog, linkedCts.Token);
                logger.LogInformation("AI response received on attempt {Attempt} with confidence {Confidence}.", attempt + 1, result.Confidence);

                if (result.Confidence >= AcceptedConfidenceThreshold)
                {
                    return new AnalysisOutcome(result, IsLowConfidence: false);
                }

                lowConfidenceResult = result;
                logger.LogWarning("AI returned low confidence ({Confidence}) on attempt {Attempt}.", result.Confidence, attempt + 1);
            }
            catch (OperationCanceledException ex) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                lastError = new TimeoutException("AI analysis timed out.", ex);
                logger.LogWarning(lastError, "AI timed out on attempt {Attempt}.", attempt + 1);
            }
            catch (Exception ex)
            {
                lastError = ex;
                logger.LogWarning(ex, "AI analysis failed on attempt {Attempt}.", attempt + 1);
            }
        }

        if (lowConfidenceResult is not null)
        {
            logger.LogInformation("Returning AI result with low confidence {Confidence} without fallback.", lowConfidenceResult.Confidence);
            return new AnalysisOutcome(lowConfidenceResult, IsLowConfidence: true);
        }

        var fallbackReason = lastError ?? new InvalidOperationException("AI analysis failed.");
        logger.LogWarning(fallbackReason, "Fallback triggered due to AI exception/timeout/parsing failure.");
        return new AnalysisOutcome(CreateFallbackResult(processedLog, fallbackReason), IsLowConfidence: false);
    }

    private static List<string> SplitLogEntries(string logs)
    {
        var entries = logs
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        return entries.Count == 0 ? [logs] : entries;
    }

    private static LogAnalysisResponse AggregateResponses(IReadOnlyList<LogAnalysisResponse> responses, bool allCached)
    {
        if (responses.Count == 1)
        {
            var single = responses[0];
            single.IsCached = allCached;
            return single;
        }

        var primary = responses
            .OrderByDescending(x => SeverityRank(x.Severity))
            .ThenByDescending(x => x.Confidence)
            .First();

        return new LogAnalysisResponse
        {
            Severity = primary.Severity,
            Category = primary.Category,
            Summary = string.Join(" | ", responses.Select(x => x.Summary).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct()),
            Suggestion = primary.Suggestion,
            Confidence = responses.Average(x => x.Confidence),
            GroupId = primary.GroupId,
            IsCached = allCached,
            IsLowConfidence = responses.All(x => x.IsLowConfidence),
            RawAIResponse = string.Join(
                " | ",
                responses
                    .Select(x => x.RawAIResponse)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct())
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

    private sealed record AnalysisOutcome(LogAnalysisResult Result, bool IsLowConfidence);
}
