namespace LogAnalyzer.Domain.Interfaces;

public interface ILogGroupingService
{
    string CreateGroupId(string log);
}
