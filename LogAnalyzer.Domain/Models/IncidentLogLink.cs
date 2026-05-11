namespace LogAnalyzer.Domain.Models;

public class IncidentLogLink
{
    public int Id { get; set; }

    public int IncidentId { get; set; }

    public Incident Incident { get; set; } = null!;

    public string LogHash { get; set; } = string.Empty;

    public DateTime LinkedUtc { get; set; }
}
