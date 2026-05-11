using LogAnalyzer.Domain.Interfaces;
using LogAnalyzer.Infrastructure.Jira;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LogAnalyzer.Infrastructure.Outbound;

public static class OutboundServiceCollectionExtensions
{
    public static IServiceCollection AddLogAnalyzerOutboundIntegration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<JiraOptions>(configuration.GetSection(JiraOptions.SectionName));

        services.AddSingleton<OutboundIntegrationMetrics>();
        services.AddSingleton<IJiraTicketEnqueuePolicy, JiraTicketEnqueuePolicy>();
        services.AddSingleton<IJiraIssueDescriptionFormatter, JiraIssueDescriptionFormatter>();
        services.AddSingleton<JiraOutboundCreateExecutor>();

        services.AddHttpClient<JiraRestIssueService>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<JiraOptions>>().Value;
            JiraHttpClientConfigurator.Apply(client, opts);
        });
        services.AddHttpClient<JiraIssueQueryService>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<JiraOptions>>().Value;
            JiraHttpClientConfigurator.Apply(client, opts);
        });

        services.AddTransient<MockJiraIssueService>();
        services.AddTransient<IJiraIssueService>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<JiraOptions>>().Value;
            return opts.UseMockClient ? sp.GetRequiredService<MockJiraIssueService>() : sp.GetRequiredService<JiraRestIssueService>();
        });

        services.AddSingleton<ChannelOutboundWorkQueue>();
        services.AddSingleton<IOutboundWorkQueue>(sp => sp.GetRequiredService<ChannelOutboundWorkQueue>());
        services.AddHostedService<OutboundDispatcherHostedService>();
        services.AddScoped<IIncidentOutboundEnqueueCoordinator, IncidentOutboundEnqueueCoordinator>();

        services.AddHealthChecks()
            .AddCheck<OutboundIntegrationHealthCheck>("outbound_jira", tags: ["ready"]);

        return services;
    }
}
