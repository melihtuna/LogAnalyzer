namespace LogAnalyzer.Infrastructure.Options;

/// <summary>
/// Version metadata persisted on incidents until Semantic Kernel / richer pipelines own these values.
/// </summary>
public sealed class IncidentAiSnapshotOptions
{
    public const string SectionName = "IncidentAiSnapshot";

    public string PromptVersion { get; set; } = "1";

    public string PipelineVersion { get; set; } = "1";

    /// <summary>
    /// Overrides OpenAI model id when set; otherwise configuration key OpenAI:Model is used if present.
    /// </summary>
    public string? AiModel { get; set; }
}
