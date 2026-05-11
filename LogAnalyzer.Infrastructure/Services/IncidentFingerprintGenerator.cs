using System.Security.Cryptography;
using System.Text;
using LogAnalyzer.Domain.Interfaces;

namespace LogAnalyzer.Infrastructure.Services;

public sealed class IncidentFingerprintGenerator : IIncidentFingerprintGenerator
{
    public string CurrentFingerprintVersion => "1";

    public IncidentFingerprintValue Compute(string groupId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);

        var payload = $"{CurrentFingerprintVersion}:{groupId}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        return new IncidentFingerprintValue(hex, CurrentFingerprintVersion);
    }
}
