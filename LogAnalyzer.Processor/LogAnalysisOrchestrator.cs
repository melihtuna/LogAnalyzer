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
    private static readonly TimeSpan AiTimeout = TimeSpan.FromSeconds(3);

    public async Task<LogAnalysisResponse> AnalyzeAsync(LogRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Logs))
        {
            throw new ArgumentException("Logs payload is required.", nameof(request));
        }

        var logEntries = SplitLogEntries(request.Logs);
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
            var aiResult = await AnalyzeWithRetryAsync(processedLog, cancellationToken);

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
            responses.Add(Map(record, isCached: false));

            if (string.Equals(record.Severity, "critical", StringComparison.OrdinalIgnoreCase))
            {
                _ = notificationService.NotifyCriticalAsync(record, CancellationToken.None);
            }
        }

        await repository.SaveChangesAsync(cancellationToken);
        return AggregateResponses(responses, allCached);
    }

    private static LogAnalysisResponse Map(LogAnalysisRecord record, bool isCached)
    {
        return new LogAnalysisResponse
        {
            Severity = record.Severity,
            Category = record.Category,
            Summary = record.Summary,
            Suggestion = record.Suggestion,
            Confidence = record.Confidence,
            GroupId = record.GroupId,
            IsCached = isCached
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

    private async Task<LogAnalysisResult> AnalyzeWithRetryAsync(string processedLog, CancellationToken cancellationToken)
    {
        Exception? lastError = null;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var timeoutCts = new CancellationTokenSource(AiTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                var result = await logAnalyzerAi.AnalyzeAsync(processedLog, linkedCts.Token);
                if (result.Confidence >= 0.5)
                {
                    return result;
                }

                lastError = new InvalidOperationException("AI confidence below threshold.");
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

        return CreateFallbackResult(processedLog, lastError ?? new InvalidOperationException("AI analysis failed."));
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
            IsCached = allCached
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
}
