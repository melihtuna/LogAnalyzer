using System.Security.Cryptography;
using System.Text;
using LogAnalyzer.Domain.Interfaces;

namespace LogAnalyzer.Infrastructure.Services;

public class Sha256LogFingerprintService : ILogFingerprintService
{
    public string ComputeHash(string log)
    {
        var bytes = Encoding.UTF8.GetBytes(log ?? string.Empty);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    public string ComputeStableHash(string log)
    {
        var normalized = LogFingerprintNormalizer.NormalizeForStableFingerprint(log ?? string.Empty);
        var bytes = Encoding.UTF8.GetBytes(normalized);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
