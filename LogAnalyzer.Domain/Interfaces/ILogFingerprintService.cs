namespace LogAnalyzer.Domain.Interfaces;

public interface ILogFingerprintService
{
    string ComputeHash(string log);
}
