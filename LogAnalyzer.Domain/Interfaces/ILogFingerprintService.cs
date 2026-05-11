namespace LogAnalyzer.Domain.Interfaces;

public interface ILogFingerprintService
{
    /// <summary>Exact SHA-256 of the input (used for whole-batch snapshots, e.g. “unchanged logs” detection).</summary>
    string ComputeHash(string log);

    /// <summary>SHA-256 after normalizing volatile segments (timestamps in known positions, correlation ids) for deduplication and DB cache keys.</summary>
    string ComputeStableHash(string log);
}
