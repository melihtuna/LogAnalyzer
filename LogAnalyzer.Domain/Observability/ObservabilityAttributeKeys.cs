namespace LogAnalyzer.Domain.Observability;

/// <summary>
/// Shared OpenTelemetry attribute / structured logging scope keys (cross-cutting).
/// </summary>
public static class ObservabilityAttributeKeys
{
    public const string ClassificationSchemaVersion = "classification.schema_version";

    public const string ClassificationParseMode = "classification.parse_mode";

    public const string ClassificationFallbackUsed = "classification.fallback_used";

    public const string ClassificationRetryCount = "classification.retry_count";

    public const string ClassificationConfidence = "classification.confidence";

    public const string ClassificationConfidenceParseFailed = "classification.confidence_parse_failed";

    public const string ClassificationConfidenceClamped = "classification.confidence_clamped";

    public const string ClassificationEnumFallback = "classification.enum_fallback";

    public const string IncidentId = "incident.id";

    public const string IncidentFingerprint = "incident.fingerprint";

    public const string IncidentStatus = "incident.status";

    public const string PipelineVersion = "pipeline.version";

    public const string OutboundQueueOverflow = "outbound.queue_overflow";

    public const string JiraOperation = "jira.operation";

    public const string JiraDispatchOutcome = "jira.dispatch_outcome";

    public const string JiraIssueKey = "jira.issue_key";

    public const string JiraRetryAttempt = "jira.retry_attempt";

    public const string JiraRetryMaxAttempts = "jira.retry_max_attempts";

    public const string JiraRetryClassification = "jira.retry_classification";

    public const string JiraHttpStatusCode = "jira.http_status_code";
}
