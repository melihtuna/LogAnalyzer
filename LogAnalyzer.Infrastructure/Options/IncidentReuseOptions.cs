namespace LogAnalyzer.Infrastructure.Options;

public sealed class IncidentReuseOptions
{
    public const string SectionName = "IncidentReuse";

    /// <summary>
    /// Incidents with the same fingerprint and Status != Closed within this window are merged.
    /// </summary>
    public int ReuseWindowMinutes { get; set; } = 1440;
}
