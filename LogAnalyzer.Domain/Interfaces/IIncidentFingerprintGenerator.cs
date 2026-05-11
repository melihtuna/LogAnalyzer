namespace LogAnalyzer.Domain.Interfaces;

/// <summary>
/// Builds a stable incident fingerprint from <paramref name="groupId"/> and a versioned algorithm (no AI inputs).
/// </summary>
public interface IIncidentFingerprintGenerator
{
    /// <summary>
    /// Current algorithm label persisted on incidents (<see cref="Models.Incident.FingerprintVersion"/>).
    /// </summary>
    string CurrentFingerprintVersion { get; }

    IncidentFingerprintValue Compute(string groupId);
}

public sealed record IncidentFingerprintValue(string Fingerprint, string FingerprintVersion);
