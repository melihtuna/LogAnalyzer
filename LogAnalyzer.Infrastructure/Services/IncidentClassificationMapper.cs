using LogAnalyzer.Domain.Models;

namespace LogAnalyzer.Infrastructure.Services;

/// <summary>
/// Maps legacy AI string labels onto structured incident enums until the classification pipeline standardizes outputs.
/// </summary>
public static class IncidentClassificationMapper
{
    public static IncidentCategory MapCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return IncidentCategory.Unknown;
        }

        return category.Trim().ToLowerInvariant() switch
        {
            "backend" or "application" => IncidentCategory.Backend,
            "ui" => IncidentCategory.UI,
            "database" => IncidentCategory.Database,
            "infrastructure" => IncidentCategory.Infrastructure,
            "auth" or "authentication" => IncidentCategory.Authentication,
            "external_service" or "external" or "external service" or "externalservice" => IncidentCategory.ExternalService,
            "network" => IncidentCategory.Network,
            "timeout" => IncidentCategory.Infrastructure,
            _ => IncidentCategory.Unknown
        };
    }

    public static IncidentSeverity MapSeverity(string? severity)
    {
        if (string.IsNullOrWhiteSpace(severity))
        {
            return IncidentSeverity.Low;
        }

        return severity.Trim().ToLowerInvariant() switch
        {
            "critical" => IncidentSeverity.Critical,
            "high" => IncidentSeverity.High,
            "medium" => IncidentSeverity.Medium,
            _ => IncidentSeverity.Low
        };
    }
}
