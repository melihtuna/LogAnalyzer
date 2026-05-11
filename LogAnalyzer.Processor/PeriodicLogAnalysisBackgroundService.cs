using System.Text.Json;
using LogAnalyzer.Domain.Interfaces;
using LogAnalyzer.Domain.Models;
using LogAnalyzer.Infrastructure.Jira;
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
            var incidentFingerprintGenerator = scope.ServiceProvider.GetRequiredService<IIncidentFingerprintGenerator>();

            var cachedRecords = new List<LogAnalysisRecord>();
            var uncachedLines = new List<string>();
            ClassificationCorrelationSnapshot? batchCorrelationForIncident = null;
            var operationalGroupUpserts =
                new List<(LogAnalysisRecord Record, int OccurrenceIncrement, string GroupId, string OperationalTitleHint,
                    string EvidenceLineHashes)>();

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
                        "Periodic OpenAI input truncated from {Original} to {Limited} uncached lines to reduce rate limits.",
                        uncachedLines.Count,
                        maxForOpenAi);
                }

                var maxOpenAiCalls = configuration.GetValue("PeriodicAnalysis:MaxOpenAiCallsPerCycle", 16);
                maxOpenAiCalls = Math.Clamp(maxOpenAiCalls, 1, 64);

                var partitioned = PartitionUncachedLinesIntoOperationalGroups(limitedUncached, logParser, groupingService);
                var groupAiResults = new List<LogAnalysisResult>();
                var openAiCalls = 0;
                var linesCommittedFromOpenAi = 0;

                foreach (var (_, groupLines) in partitioned)
                {
                    if (openAiCalls >= maxOpenAiCalls)
                    {
                        var deferred = limitedUncached.Count - linesCommittedFromOpenAi;
                        if (deferred > 0)
                        {
                            logger.LogWarning(
                                "Periodic operational groups: OpenAI call budget ({Budget}) reached; {Deferred} uncached line(s) left for a later cycle.",
                                maxOpenAiCalls,
                                deferred);
                        }

                        break;
                    }

                    if (groupLines.Count == 0)
                    {
                        continue;
                    }

                    openAiCalls++;
                    var orderedLines = groupLines.OrderBy(x => x, StringComparer.Ordinal).ToList();
                    var payload = string.Join(Environment.NewLine, orderedLines);
                    var processedPayload = logParser.ExtractErrorLinesOrFullLogs(payload);
                    var groupId = OperationalIncidentFingerprintHeuristics.ComputeGroupIdFromEvidenceOnly(
                        processedPayload,
                        groupingService);

                    var aiResult = await logAnalyzerAi.AnalyzeAsync(processedPayload, stoppingToken).ConfigureAwait(false);
                    groupAiResults.Add(aiResult);
                    linesCommittedFromOpenAi += orderedLines.Count;

                    foreach (var line in orderedLines)
                    {
                        var lineHash = fingerprintService.ComputeStableHash(line);
                        var lineProcessed = logParser.ExtractErrorLinesOrFullLogs(line);
                        var lineRecord = new LogAnalysisRecord
                        {
                            LogHash = lineHash,
                            GroupId = groupId,
                            OriginalLog = line,
                            ProcessedLog = lineProcessed,
                            Severity = aiResult.Severity,
                            Category = aiResult.Category,
                            Summary = aiResult.Summary,
                            Suggestion = aiResult.Suggestion,
                            PossibleRootCause = aiResult.PossibleRootCause,
                            Confidence = aiResult.Confidence,
                            CreatedUtc = DateTime.UtcNow,
                            LastSeenUtc = DateTime.UtcNow,
                            Count = 1
                        };

                        await repository.AddAsync(lineRecord, stoppingToken).ConfigureAwait(false);
                    }

                    var groupLinkHash = ComputeOperationalGroupLinkHash(orderedLines, fingerprintService);
                    var evidenceHashes = string.Join(
                        ",",
                        orderedLines.Select(l => fingerprintService.ComputeStableHash(l)));
                    var incidentRecord = new LogAnalysisRecord
                    {
                        LogHash = groupLinkHash,
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
                        Count = orderedLines.Count
                    };

                    var titleHint = DeriveOperationalPresentationTitle(aiResult);
                    operationalGroupUpserts.Add((
                        incidentRecord,
                        orderedLines.Count,
                        groupId,
                        titleHint,
                        evidenceHashes));

                    var incidentFingerprint = incidentFingerprintGenerator.Compute(groupId).Fingerprint;
                    var preview = orderedLines[0];
                    if (preview.Length > 240)
                    {
                        preview = preview[..240];
                    }

                    logger.LogInformation(
                        "Periodic operational group: group_id={GroupId} incident_fingerprint={IncidentFingerprint} line_count={LineCount} openai_calls_so_far={Calls} title_hint={TitleHint} line_hashes={Hashes} first_line_preview={Preview}",
                        groupId,
                        incidentFingerprint,
                        orderedLines.Count,
                        openAiCalls,
                        titleHint,
                        evidenceHashes,
                        preview);
                }

                var mergedNewAi = MergeMultipleAiResults(groupAiResults);
                batchCorrelationForIncident = groupAiResults.Count > 0
                    ? new ClassificationCorrelationSnapshot(
                        mergedNewAi.ClassificationParseMode,
                        mergedNewAi.ClassificationFallbackUsed)
                    : null;

                analysis = cachedRecords.Count > 0
                    ? MergeAiWithCached(mergedNewAi, cachedRecords)
                    : mergedNewAi;
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
                    presentation: null,
                    cancellationToken: stoppingToken).ConfigureAwait(false);
                outboundCoordinator.TrackIncidentFingerprint(cachedUpsert.IncidentFingerprint);
            }

            foreach (var (record, occurrenceIncrement, groupId, titleHint, evidenceHashes) in operationalGroupUpserts)
            {
                var evidenceExcerpt = TruncateForIncidentEvidence(
                    record.ProcessedLog,
                    JiraIssueFormattingLimits.EvidenceLogExcerptMaxLength);
                var presentation = new IncidentUpsertPresentation(titleHint, evidenceExcerpt);
                var upsert = await incidentUpsert.UpsertFromLogAnalysisAsync(
                    record,
                    IncidentSource.PeriodicGraylog,
                    occurrenceIncrement: occurrenceIncrement,
                    classificationCorrelation: batchCorrelationForIncident,
                    presentation: presentation,
                    cancellationToken: stoppingToken).ConfigureAwait(false);
                outboundCoordinator.TrackIncidentFingerprint(upsert.IncidentFingerprint);
                logger.LogInformation(
                    "Periodic operational group upsert: title_hint={TitleHint} category={Category} group_id={GroupId} incident_fingerprint={IncidentFingerprint} was_created={WasCreated} occurrence_increment={Occ} evidence_line_hashes={EvidenceHashes}",
                    titleHint,
                    record.Category,
                    groupId,
                    upsert.IncidentFingerprint,
                    upsert.WasCreated,
                    occurrenceIncrement,
                    evidenceHashes);
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

    private static List<(string BucketKey, List<string> Lines)> PartitionUncachedLinesIntoOperationalGroups(
        IReadOnlyList<string> lines,
        ILogParser logParser,
        ILogGroupingService groupingService)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var proc = logParser.ExtractErrorLinesOrFullLogs(line);
            var bucket = OperationalIncidentFingerprintHeuristics.ComputeGroupIdFromEvidenceOnly(proc, groupingService);
            if (!map.TryGetValue(bucket, out var list))
            {
                list = new List<string>();
                map[bucket] = list;
            }

            list.Add(line);
        }

        return map.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (kv.Key, kv.Value)).ToList();
    }

    private static string ComputeOperationalGroupLinkHash(
        IReadOnlyList<string> orderedLines,
        ILogFingerprintService fingerprintService)
    {
        var joined = string.Join(
            '\u001e',
            orderedLines.Select(fingerprintService.ComputeStableHash).OrderBy(x => x, StringComparer.Ordinal));
        return fingerprintService.ComputeStableHash(joined);
    }

    private static string DeriveOperationalPresentationTitle(LogAnalysisResult aiResult)
    {
        var s = (aiResult.Summary ?? string.Empty).Trim();
        if (s.Length == 0)
        {
            return $"{aiResult.Severity} / {aiResult.Category}".Trim();
        }

        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ");
        const int max = 160;
        if (s.Length <= max)
        {
            return s;
        }

        var cut = s[..max].TrimEnd();
        return cut.Length == 0 ? s[..max] : cut + "...";
    }

    private static LogAnalysisResult MergeMultipleAiResults(IReadOnlyList<LogAnalysisResult> results)
    {
        if (results.Count == 0)
        {
            return new LogAnalysisResult
            {
                Severity = "low",
                Category = "unknown",
                Summary = "No OpenAI results in this cycle.",
                Suggestion = "N/A",
                PossibleRootCause = "N/A",
                Confidence = 0.1,
                RawAIResponse = string.Empty
            };
        }

        if (results.Count == 1)
        {
            return results[0];
        }

        var ordered = results
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
                results.Select(r => r.Summary).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct()),
            Suggestion = primary.Suggestion,
            PossibleRootCause = string.Join(
                " | ",
                results.Select(r => r.PossibleRootCause).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct()),
            Confidence = results.Average(r => r.Confidence),
            RawAIResponse = string.Join(
                " || ",
                results.Select(r => r.RawAIResponse).Where(s => !string.IsNullOrWhiteSpace(s)).Take(3)),
            ClassificationSchemaVersion = primary.ClassificationSchemaVersion,
            ClassificationParseMode = primary.ClassificationParseMode,
            ClassificationFallbackUsed = primary.ClassificationFallbackUsed,
            ClassificationRetryCount = results.Max(r => r.ClassificationRetryCount)
        };
    }

    private static string TruncateForIncidentEvidence(string text, int maxChars)
    {
        var t = (text ?? string.Empty).Trim();
        if (t.Length <= maxChars)
        {
            return t;
        }

        const string ell = "...";
        var take = Math.Max(0, maxChars - ell.Length);
        return string.Concat(t.AsSpan(0, take), ell);
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
