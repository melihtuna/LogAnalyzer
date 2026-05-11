namespace LogAnalyzer.Domain.Models;

/// <summary>Optional classification metadata propagated into incident upsert observability.</summary>
public readonly record struct ClassificationCorrelationSnapshot(string? ParseMode, string? FallbackUsed);
