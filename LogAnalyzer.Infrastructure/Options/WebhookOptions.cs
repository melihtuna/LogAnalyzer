namespace LogAnalyzer.Infrastructure.Options;

public class WebhookOptions
{
    public const string SectionName = "Webhook";

    public string? Url { get; set; }
}
