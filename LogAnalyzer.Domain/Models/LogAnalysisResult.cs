namespace LogAnalyzer.Domain.Models;

public class LogAnalysisResult
{
    public string Severity { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    /// <summary>Canonical v1 technical_summary (stored historically as Summary).</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Canonical v1 recommended_action (stored historically as Suggestion).</summary>
    public string Suggestion { get; set; } = string.Empty;

    public string PossibleRootCause { get; set; } = string.Empty;

    public double Confidence { get; set; }

    /// <summary>Primary completion assistant content (audit).</summary>
    public string RawAIResponse { get; set; } = string.Empty;

    /// <summary>Set when a single repair completion was used for structured parsing.</summary>
    public string? ClassificationRepairRawResponse { get; set; }

    public string ClassificationSchemaVersion { get; set; } = string.Empty;

    public string ClassificationParseMode { get; set; } = string.Empty;

    /// <summary>Telemetry: "false" | "legacy_lift" | "minimal_safe_fallback"</summary>
    public string ClassificationFallbackUsed { get; set; } = "false";

    public int ClassificationRetryCount { get; set; }

    public bool ClassificationEnumFallback { get; set; }

    public bool ClassificationConfidenceParseFailed { get; set; }

    public bool ClassificationConfidenceClamped { get; set; }
}
