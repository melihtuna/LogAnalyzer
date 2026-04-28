using System.Security.Cryptography;
using System.Text;
using LogAnalyzer.Domain.Interfaces;

namespace LogAnalyzer.Infrastructure.Services;

public class Sha256LogFingerprintService : ILogFingerprintService
{
    public string ComputeHash(string log)
    {
        var bytes = Encoding.UTF8.GetBytes(log);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
