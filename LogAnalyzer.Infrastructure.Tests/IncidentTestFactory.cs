using LogAnalyzer.Domain.Models;

namespace LogAnalyzer.Infrastructure.Tests;

internal static class IncidentTestFactory
{
    internal static Incident CreateMinimal(Action<Incident>? configure = null)
    {
        var utc = new DateTime(2026, 5, 10, 12, 0, 0, DateTimeKind.Utc);
        var incident = new Incident
        {
            Id = 42,
            IncidentFingerprint = "sha256:demo",
            FingerprintVersion = "1",
            PrimaryGroupId = "group-a",
            Status = IncidentStatus.Open,
            FirstSeenUtc = utc,
            LastSeenUtc = utc,
            OccurrenceCount = 3,
            Category = IncidentCategory.Backend,
            Severity = IncidentSeverity.High,
            TechnicalSummary = "Demo summary line.",
            PossibleRootCause = "Demo root cause.",
            RecommendedAction = "Demo action.",
            Confidence = 0.775,
            AiModel = "gpt-test",
            PromptVersion = "pv-1",
            PipelineVersion = "pl-1",
            Source = IncidentSource.ApiAdHoc,
            UpdatedUtc = utc,
        };

        configure?.Invoke(incident);
        return incident;
    }
}
