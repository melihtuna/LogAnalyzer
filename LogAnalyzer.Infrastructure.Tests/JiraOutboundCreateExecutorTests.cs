using LogAnalyzer.Domain.Interfaces;
using LogAnalyzer.Domain.Models;
using LogAnalyzer.Infrastructure.Jira;
using LogAnalyzer.Infrastructure.Outbound;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace LogAnalyzer.Infrastructure.Tests;

public sealed class JiraOutboundCreateExecutorTests
{
    [Fact]
    public async Task Skips_jira_when_external_key_already_set()
    {
        var incident = IncidentTestFactory.CreateMinimal(i =>
        {
            i.Id = 99;
            i.ExternalIssueKey = "LOG-1";
        });

        var repo = new Mock<IIncidentRepository>();
        repo.Setup(r => r.GetByIdAsync(99, It.IsAny<CancellationToken>())).ReturnsAsync(incident);

        var jira = new Mock<IJiraIssueService>();

        var services = new ServiceCollection();
        services.AddSingleton<OutboundIntegrationMetrics>();
        services.AddSingleton(repo.Object);
        services.AddSingleton(jira.Object);
        services.AddSingleton<IJiraIssueDescriptionFormatter, JiraIssueDescriptionFormatter>();
        services.AddSingleton<IJiraTicketEnqueuePolicy>(_ =>
            new JiraTicketEnqueuePolicy(Microsoft.Extensions.Options.Options.Create(new JiraOptions { EnableIntegration = true })));
        services.AddSingleton<ILogger<JiraOutboundCreateExecutor>>(NullLogger<JiraOutboundCreateExecutor>.Instance);
        services.AddSingleton<JiraOutboundCreateExecutor>();

        await using var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<JiraOutboundCreateExecutor>();

        await executor.ExecuteAsync(99, CancellationToken.None);

        jira.Verify(
            x => x.CreateIssueAsync(It.IsAny<CreateJiraIssueCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Persists_link_after_successful_create()
    {
        var incident = IncidentTestFactory.CreateMinimal(i =>
        {
            i.Id = 100;
            i.ExternalIssueKey = null;
        });

        var repo = new Mock<IIncidentRepository>();
        repo.Setup(r => r.GetByIdAsync(100, It.IsAny<CancellationToken>())).ReturnsAsync(incident);

        var jira = new Mock<IJiraIssueService>();
        jira.Setup(x => x.CreateIssueAsync(It.IsAny<CreateJiraIssueCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateJiraIssueResult("LOG-2", "https://example.atlassian.net/browse/LOG-2"));

        repo.Setup(r =>
                r.SetExternalIssueAsync(100, "LOG-2", "https://example.atlassian.net/browse/LOG-2", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton<OutboundIntegrationMetrics>();
        services.AddSingleton(repo.Object);
        services.AddSingleton(jira.Object);
        services.AddSingleton<IJiraIssueDescriptionFormatter, JiraIssueDescriptionFormatter>();
        services.AddSingleton<IJiraTicketEnqueuePolicy>(_ =>
            new JiraTicketEnqueuePolicy(Microsoft.Extensions.Options.Options.Create(new JiraOptions { EnableIntegration = true })));
        services.AddSingleton<ILogger<JiraOutboundCreateExecutor>>(NullLogger<JiraOutboundCreateExecutor>.Instance);
        services.AddSingleton<JiraOutboundCreateExecutor>();

        await using var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<JiraOutboundCreateExecutor>();

        await executor.ExecuteAsync(100, CancellationToken.None);

        jira.Verify(x => x.CreateIssueAsync(It.IsAny<CreateJiraIssueCommand>(), It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(
            r => r.SetExternalIssueAsync(100, "LOG-2", "https://example.atlassian.net/browse/LOG-2", It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
