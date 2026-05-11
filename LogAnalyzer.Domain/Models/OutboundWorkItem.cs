namespace LogAnalyzer.Domain.Models;

public readonly record struct OutboundWorkItem(OutboundWorkKind Kind, int IncidentId);
