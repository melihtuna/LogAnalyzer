using System.Diagnostics;
using LogAnalyzer.Domain.Interfaces;
using LogAnalyzer.Domain.Models;
using LogAnalyzer.Domain.Observability;
using LogAnalyzer.Infrastructure.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LogAnalyzer.Infrastructure.Services;

public sealed class IncidentUpsertService(
    IIncidentRepository incidentRepository,
    IIncidentFingerprintGenerator fingerprintGenerator,
    IOptions<IncidentReuseOptions> reuseOptions,
    IOptions<IncidentAiSnapshotOptions> aiSnapshotOptions,
    IConfiguration configuration,
    ILogger<IncidentUpsertService> logger) : IIncidentUpsertService
{
    private readonly Dictionary<string, Incident> _pendingByFingerprint = new(StringComparer.Ordinal);

    public async Task<IncidentUpsertResult> UpsertFromLogAnalysisAsync(
        LogAnalysisRecord record,
        IncidentSource source,
        int occurrenceIncrement = 1,
        ClassificationCorrelationSnapshot? classificationCorrelation = null,
        IncidentUpsertPresentation? presentation = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        var increment = Math.Max(occurrenceIncrement, 1);

        var fp = fingerprintGenerator.Compute(record.GroupId);
        var window = TimeSpan.FromMinutes(Math.Max(reuseOptions.Value.ReuseWindowMinutes, 1));

        var incident = await ResolveIncidentAsync(fp.Fingerprint, window, cancellationToken).ConfigureAwait(false);

        var now = DateTime.UtcNow;
        var aiOpts = aiSnapshotOptions.Value;
        var model = string.IsNullOrWhiteSpace(aiOpts.AiModel)
            ? configuration["OpenAI:Model"] ?? string.Empty
            : aiOpts.AiModel;

        if (incident is null)
        {
            incident = new Incident
            {
                IncidentFingerprint = fp.Fingerprint,
                FingerprintVersion = fp.FingerprintVersion,
                PrimaryGroupId = record.GroupId,
                PrimaryLogHash = record.LogHash,
                Status = IncidentStatus.Open,
                FirstSeenUtc = now,
                LastSeenUtc = now,
                OccurrenceCount = increment,
                Category = IncidentClassificationMapper.MapCategory(record.Category),
                Severity = IncidentClassificationMapper.MapSeverity(record.Severity),
                TechnicalSummary = record.Summary,
                OperationalTitle = NormalizeOptional(presentation?.OperationalTitle),
                EvidenceLogExcerpt = NormalizeOptional(presentation?.EvidenceLogExcerpt),
                PossibleRootCause = record.PossibleRootCause,
                RecommendedAction = record.Suggestion,
                Confidence = record.Confidence,
                AiModel = model,
                PromptVersion = aiOpts.PromptVersion,
                PipelineVersion = aiOpts.PipelineVersion,
                Source = source,
                UpdatedUtc = now
            };

            AddEvidenceLink(incident, record.LogHash, now);
            await incidentRepository.AddAsync(incident, cancellationToken).ConfigureAwait(false);
            _pendingByFingerprint[fp.Fingerprint] = incident;

            PublishIncidentObservability(
                incident,
                fp.Fingerprint,
                classificationCorrelation,
                aiOpts.PipelineVersion,
                wasCreated: true,
                occurrenceIncrement: increment,
                previousOccurrenceTotal: null);

            return new IncidentUpsertResult(fp.Fingerprint, WasCreated: true);
        }

        var previousTotal = incident.OccurrenceCount;
        incident.LastSeenUtc = now;
        incident.UpdatedUtc = now;
        incident.OccurrenceCount += increment;
        incident.Category = IncidentClassificationMapper.MapCategory(record.Category);
        incident.Severity = IncidentClassificationMapper.MapSeverity(record.Severity);
        incident.TechnicalSummary = record.Summary;
        if (presentation?.OperationalTitle is not null)
        {
            incident.OperationalTitle = NormalizeOptional(presentation.OperationalTitle);
        }

        if (presentation?.EvidenceLogExcerpt is not null)
        {
            incident.EvidenceLogExcerpt = NormalizeOptional(presentation.EvidenceLogExcerpt);
        }

        incident.PossibleRootCause = record.PossibleRootCause;
        incident.RecommendedAction = record.Suggestion;
        incident.Confidence = record.Confidence;
        incident.AiModel = model;
        incident.PromptVersion = aiOpts.PromptVersion;
        incident.PipelineVersion = aiOpts.PipelineVersion;
        incident.Source = source;

        AddEvidenceLink(incident, record.LogHash, now);

        _pendingByFingerprint[fp.Fingerprint] = incident;

        PublishIncidentObservability(
            incident,
            fp.Fingerprint,
            classificationCorrelation,
            aiOpts.PipelineVersion,
            wasCreated: false,
            occurrenceIncrement: increment,
            previousOccurrenceTotal: previousTotal);

        return new IncidentUpsertResult(fp.Fingerprint, WasCreated: false);
    }

    private void PublishIncidentObservability(
        Incident incident,
        string fingerprint,
        ClassificationCorrelationSnapshot? correlation,
        string pipelineVersion,
        bool wasCreated,
        int occurrenceIncrement,
        int? previousOccurrenceTotal)
    {
        var activity = Activity.Current;
        activity?.SetTag(ObservabilityAttributeKeys.IncidentFingerprint, fingerprint);
        activity?.SetTag(ObservabilityAttributeKeys.PipelineVersion, pipelineVersion);
        activity?.SetTag(ObservabilityAttributeKeys.IncidentStatus, incident.Status.ToString());
        if (incident.Id != 0)
        {
            activity?.SetTag(ObservabilityAttributeKeys.IncidentId, incident.Id);
        }

        if (correlation.HasValue)
        {
            var c = correlation.Value;
            if (!string.IsNullOrEmpty(c.ParseMode))
            {
                activity?.SetTag(ObservabilityAttributeKeys.ClassificationParseMode, c.ParseMode);
            }

            if (!string.IsNullOrEmpty(c.FallbackUsed))
            {
                activity?.SetTag(ObservabilityAttributeKeys.ClassificationFallbackUsed, c.FallbackUsed);
            }
        }

        var scopeState = new Dictionary<string, object?>
        {
            [ObservabilityAttributeKeys.IncidentFingerprint] = fingerprint,
            [ObservabilityAttributeKeys.PipelineVersion] = pipelineVersion,
            [ObservabilityAttributeKeys.IncidentStatus] = incident.Status.ToString(),
            [ObservabilityAttributeKeys.IncidentId] = incident.Id != 0 ? incident.Id : null
        };

        if (correlation.HasValue)
        {
            var c = correlation.Value;
            if (!string.IsNullOrEmpty(c.ParseMode))
            {
                scopeState[ObservabilityAttributeKeys.ClassificationParseMode] = c.ParseMode;
            }

            if (!string.IsNullOrEmpty(c.FallbackUsed))
            {
                scopeState[ObservabilityAttributeKeys.ClassificationFallbackUsed] = c.FallbackUsed;
            }
        }

        using (logger.BeginScope(scopeState))
        {
            if (wasCreated)
            {
                logger.LogDebug(
                    "Incident reuse decision: created new incident fingerprint={Fingerprint} occurrence_increment={Increment} occurrence_total={Total} reuse_window_minutes={ReuseWindow}",
                    fingerprint,
                    occurrenceIncrement,
                    incident.OccurrenceCount,
                    reuseOptions.Value.ReuseWindowMinutes);
            }
            else
            {
                logger.LogDebug(
                    "Incident reuse decision: reused active incident fingerprint={Fingerprint} incident_id={IncidentId} occurrence_increment={Increment} occurrence_previous={Previous} occurrence_total={Total} reuse_window_minutes={ReuseWindow}",
                    fingerprint,
                    incident.Id != 0 ? incident.Id : null,
                    occurrenceIncrement,
                    previousOccurrenceTotal,
                    incident.OccurrenceCount,
                    reuseOptions.Value.ReuseWindowMinutes);
            }
        }
    }

    private async Task<Incident?> ResolveIncidentAsync(
        string fingerprint,
        TimeSpan window,
        CancellationToken cancellationToken)
    {
        if (_pendingByFingerprint.TryGetValue(fingerprint, out var cached))
        {
            logger.LogDebug(
                "Incident fingerprint resolved from in-memory pending cache fingerprint={Fingerprint}",
                fingerprint);
            return cached;
        }

        return await incidentRepository
            .FindActiveWithinWindowAsync(fingerprint, window, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var t = value.Trim();
        return t.Length == 0 ? null : t;
    }

    private static void AddEvidenceLink(Incident incident, string logHash, DateTime linkedUtc)
    {
        if (string.IsNullOrWhiteSpace(logHash))
        {
            return;
        }

        if (incident.LogLinks.Any(l => string.Equals(l.LogHash, logHash, StringComparison.Ordinal)))
        {
            return;
        }

        incident.LogLinks.Add(new IncidentLogLink
        {
            LogHash = logHash,
            LinkedUtc = linkedUtc,
            Incident = incident
        });
    }
}
