using System.Text.Json;
using LogAnalyzer.Domain.Interfaces;
using LogAnalyzer.Domain.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LogAnalyzer.Processor;

public class PeriodicLogAnalysisBackgroundService(
    ILogProvider logProvider,
    ILogFingerprintService fingerprintService,
    ILogAnalyzerAI logAnalyzerAi,
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<PeriodicLogAnalysisBackgroundService> logger) : BackgroundService
{
    private string? lastProcessedLogHash;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMinutes = configuration.GetValue("PeriodicAnalysis:IntervalMinutes", 15);
        intervalMinutes = Math.Clamp(intervalMinutes, 1, 1440);
        var maxDistinct = configuration.GetValue("PeriodicAnalysis:MaxDistinctLines", 60);
        var maxForOpenAi = configuration.GetValue("PeriodicAnalysis:MaxLinesSentToOpenAi", 12);
        maxForOpenAi = Math.Clamp(maxForOpenAi, 1, 64);
        maxDistinct = Math.Max(1, maxDistinct);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(intervalMinutes));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await RunAnalysisCycleAsync(
                    maxDistinct,
                    maxForOpenAi,
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Periodic log analysis failed.");
            }
        }
    }

    private async Task RunAnalysisCycleAsync(int maxDistinct, int maxForOpenAi, CancellationToken stoppingToken)
    {
        var logs = await logProvider.GetLogsAsync();
        if (string.IsNullOrWhiteSpace(logs))
        {
            logger.LogInformation("Periodic analysis skipped because provider returned empty logs.");
            return;
        }

        var currentLogHash = fingerprintService.ComputeHash(logs);
        if (string.Equals(currentLogHash, lastProcessedLogHash, StringComparison.Ordinal))
        {
            logger.LogInformation("Periodic analysis skipped because logs are unchanged.");
            return;
        }

        var distinctLines = DistinctLinesByLogHash(logs, fingerprintService, maxDistinct, logger);
        if (distinctLines.Count == 0)
        {
            logger.LogInformation("Periodic analysis skipped after deduplication produced no lines.");
            return;
        }

        var rawForRun = string.Join(Environment.NewLine, distinctLines);
        LogAnalysisResult analysis;

        using (var scope = scopeFactory.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<ILogAnalysisRepository>();
            var logParser = scope.ServiceProvider.GetRequiredService<ILogParser>();
            var groupingService = scope.ServiceProvider.GetRequiredService<ILogGroupingService>();

            var cachedRecords = new List<LogAnalysisRecord>();
            var uncachedLines = new List<string>();
            LogAnalysisRecord? batchRecordForIncident = null;
            var batchOccurrenceIncrement = 0;
            ClassificationCorrelationSnapshot? batchCorrelationForIncident = null;

            foreach (var line in distinctLines)
            {
                var hash = fingerprintService.ComputeStableHash(line);
                var existing = await repository.GetByHashAsync(hash, stoppingToken);
                if (existing is not null)
                {
                    existing.Count += 1;
                    existing.LastSeenUtc = DateTime.UtcNow;
                    repository.Update(existing);
                    cachedRecords.Add(existing);
                    continue;
                }

                uncachedLines.Add(line);
            }

            if (uncachedLines.Count == 0)
            {
                analysis = BuildAggregateResultFromRecords(cachedRecords);
                logger.LogInformation(
                    "Periodic analysis used cache only for {Count} distinct log lines.",
                    cachedRecords.Count);
            }
            else
            {
                var limitedUncached = uncachedLines.Take(maxForOpenAi).ToList();
                if (uncachedLines.Count > maxForOpenAi)
                {
                    logger.LogWarning(
                        "Periodic OpenAI batch truncated from {Original} to {Limited} uncached lines to reduce rate limits.",
                        uncachedLines.Count,
                        maxForOpenAi);
                }

                // Stable ordering so batch fingerprint matches across cycles for the same line multiset.
                var batchLines = limitedUncached.OrderBy(x => x, StringComparer.Ordinal).ToList();
                var payload = string.Join(Environment.NewLine, batchLines);
                var batchHash = fingerprintService.ComputeStableHash(payload);

                var batchExisting = await repository.GetByHashAsync(batchHash, stoppingToken);
                LogAnalysisResult aiResult;

                if (batchExisting is not null)
                {
                    batchExisting.Count += batchLines.Count;
                    batchExisting.LastSeenUtc = DateTime.UtcNow;
                    repository.Update(batchExisting);
                    batchRecordForIncident = batchExisting;
                    batchOccurrenceIncrement = batchLines.Count;
                    batchCorrelationForIncident = null;
                    aiResult = RecordToAnalysisResult(batchExisting);
                    logger.LogInformation(
                        "Periodic analysis reused batch cache row for {LineCount} lines (single LogAnalyses row per OpenAI batch).",
                        batchLines.Count);
                }
                else
                {
                    aiResult = await logAnalyzerAi.AnalyzeAsync(payload, stoppingToken);
                    var processedPayload = logParser.ExtractErrorLinesOrFullLogs(payload);
                    var groupId = groupingService.CreateGroupId(processedPayload);
                    var newBatchRecord = new LogAnalysisRecord
                    {
                        LogHash = batchHash,
                        GroupId = groupId,
                        OriginalLog = payload,
                        ProcessedLog = processedPayload,
                        Severity = aiResult.Severity,
                        Category = aiResult.Category,
                        Summary = aiResult.Summary,
                        Suggestion = aiResult.Suggestion,
                        PossibleRootCause = aiResult.PossibleRootCause,
                        Confidence = aiResult.Confidence,
                        CreatedUtc = DateTime.UtcNow,
                        LastSeenUtc = DateTime.UtcNow,
                        Count = batchLines.Count
                    };
                    await repository.AddAsync(newBatchRecord, stoppingToken);
                    batchRecordForIncident = newBatchRecord;
                    batchOccurrenceIncrement = batchLines.Count;
                    batchCorrelationForIncident = new ClassificationCorrelationSnapshot(
                        aiResult.ClassificationParseMode,
                        aiResult.ClassificationFallbackUsed);
                    logger.LogInformation(
                        "Periodic analysis stored one batch cache row for {LineCount} uncached lines.",
                        batchLines.Count);
                }

                analysis = cachedRecords.Count > 0
                    ? MergeAiWithCached(aiResult, cachedRecords)
                    : aiResult;
            }

            var incidentUpsert = scope.ServiceProvider.GetRequiredService<IIncidentUpsertService>();
            var outboundCoordinator = scope.ServiceProvider.GetRequiredService<IIncidentOutboundEnqueueCoordinator>();

            foreach (var cached in cachedRecords)
            {
                var cachedUpsert = await incidentUpsert.UpsertFromLogAnalysisAsync(
                    cached,
                    IncidentSource.PeriodicGraylog,
                    occurrenceIncrement: 1,
                    classificationCorrelation: null,
                    stoppingToken).ConfigureAwait(false);
                outboundCoordinator.TrackIncidentFingerprint(cachedUpsert.IncidentFingerprint);
            }

            if (batchRecordForIncident is not null)
            {
                var batchUpsert = await incidentUpsert.UpsertFromLogAnalysisAsync(
                    batchRecordForIncident,
                    IncidentSource.PeriodicGraylog,
                    occurrenceIncrement: batchOccurrenceIncrement,
                    classificationCorrelation: batchCorrelationForIncident,
                    stoppingToken).ConfigureAwait(false);
                outboundCoordinator.TrackIncidentFingerprint(batchUpsert.IncidentFingerprint);
            }

            await repository.SaveChangesAsync(stoppingToken);
            await outboundCoordinator.FlushAfterPersistenceAsync(stoppingToken).ConfigureAwait(false);

            var logAnalysisRunRepository = scope.ServiceProvider.GetRequiredService<ILogAnalysisRunRepository>();
            var serializedAnalysis = JsonSerializer.Serialize(analysis);
            await logAnalysisRunRepository.AddAsync(new LogAnalysis
            {
                Timestamp = DateTime.UtcNow,
                RawLogs = rawForRun,
                AnalysisResult = serializedAnalysis
            }, stoppingToken);
            await logAnalysisRunRepository.SaveChangesAsync(stoppingToken);
        }

        lastProcessedLogHash = currentLogHash;
        Console.WriteLine($"[{DateTime.UtcNow:O}] Periodic log analysis: {JsonSerializer.Serialize(analysis)}");
    }

    private static List<string> DistinctLinesByLogHash(
        string logs,
        ILogFingerprintService fingerprintService,
        int maxLines,
        ILogger logger)
    {
        var lines = logs
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        if (lines.Count == 0)
        {
            lines = [logs.Trim()];
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var line in lines)
        {
            var hash = fingerprintService.ComputeStableHash(line);
            if (!seen.Add(hash))
            {
                continue;
            }

            result.Add(line);
            if (result.Count < maxLines)
            {
                continue;
            }

            if (lines.Count > result.Count)
            {
                logger.LogWarning(
                    "Periodic distinct log lines capped at {Max} (additional lines dropped for this cycle).",
                    maxLines);
            }

            break;
        }

        return result;
    }

    private static LogAnalysisResult RecordToAnalysisResult(LogAnalysisRecord record)
    {
        return new LogAnalysisResult
        {
            Severity = record.Severity,
            Category = record.Category,
            Summary = record.Summary,
            Suggestion = record.Suggestion,
            PossibleRootCause = record.PossibleRootCause,
            Confidence = record.Confidence,
            RawAIResponse = string.Empty
        };
    }

    private static LogAnalysisResult BuildAggregateResultFromRecords(IReadOnlyList<LogAnalysisRecord> records)
    {
        if (records.Count == 0)
        {
            return new LogAnalysisResult
            {
                Severity = "low",
                Category = "unknown",
                Summary = "No cached analysis records available.",
                Suggestion = "N/A",
                Confidence = 0.1,
                RawAIResponse = string.Empty
            };
        }

        var ordered = records
            .OrderByDescending(r => SeverityRank(r.Severity))
            .ThenByDescending(r => r.Confidence)
            .ToList();
        var primary = ordered.First();
        return new LogAnalysisResult
        {
            Severity = primary.Severity,
            Category = primary.Category,
            Summary = string.Join(
                " | ",
                records.Select(r => r.Summary).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct()),
            Suggestion = primary.Suggestion,
            PossibleRootCause = string.Join(
                " | ",
                records.Select(r => r.PossibleRootCause).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct()),
            Confidence = records.Average(r => r.Confidence),
            RawAIResponse = string.Empty
        };
    }

    private static LogAnalysisResult MergeAiWithCached(LogAnalysisResult ai, IReadOnlyList<LogAnalysisRecord> cached)
    {
        if (cached.Count == 0)
        {
            return ai;
        }

        var worstCached = cached.OrderByDescending(r => SeverityRank(r.Severity)).First();
        var severity = SeverityRank(ai.Severity) >= SeverityRank(worstCached.Severity) ? ai.Severity : worstCached.Severity;
        return new LogAnalysisResult
        {
            Severity = severity,
            Category = ai.Category,
            Summary = string.Join(
                " | ",
                new[] { ai.Summary }
                    .Concat(cached.Select(c => c.Summary))
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct()),
            Suggestion = ai.Suggestion,
            PossibleRootCause = string.Join(
                " | ",
                new[] { ai.PossibleRootCause }
                    .Concat(cached.Select(c => c.PossibleRootCause))
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct()),
            Confidence = (ai.Confidence + cached.Average(c => c.Confidence)) / 2.0,
            RawAIResponse = ai.RawAIResponse
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
