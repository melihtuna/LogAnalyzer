using System;
using System.Net;
using System.Text;
using LogAnalyzer.Infrastructure.Jira;
using LogAnalyzer.Infrastructure.Outbound;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
namespace LogAnalyzer.Infrastructure.Tests;

public sealed class JiraRestIssueServiceOperationalTests
{
    [Fact]
    public async Task Success_body_without_key_throws_InvalidOperationException()
    {
        using var handler = new OperationalSingleResponseHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            });

        using var client = new HttpClient(handler, disposeHandler: false);
        using var metrics = new OutboundIntegrationMetrics();

        var opts = Microsoft.Extensions.Options.Options.Create(new JiraOptions
        {
            BaseUrl = "https://example.atlassian.net",
            ProjectKey = "LOG",
            Email = "user@example.com",
            ApiToken = "token",
            AuthKind = JiraAuthKind.BasicEmailApiToken,
            MaxRetries = 1,
            RetryBaseMilliseconds = 0,
        });

        JiraHttpClientConfigurator.Apply(client, opts.Value);

        var svc = new JiraRestIssueService(client, opts, metrics, NullLogger<JiraRestIssueService>.Instance);
        var cmd = new CreateJiraIssueCommand(1, "s", "d");

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CreateIssueAsync(cmd, CancellationToken.None));
        Assert.Equal(1, handler.SendCount);
    }

    [Fact]
    public async Task Unauthorized_does_not_retry()
    {
        using var handler = new OperationalSingleResponseHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{\"error\":\"auth\"}", Encoding.UTF8, "application/json"),
            });

        using var client = new HttpClient(handler, disposeHandler: false);
        using var metrics = new OutboundIntegrationMetrics();

        var opts = Microsoft.Extensions.Options.Options.Create(new JiraOptions
        {
            BaseUrl = "https://example.atlassian.net",
            ProjectKey = "LOG",
            Email = "user@example.com",
            ApiToken = "token",
            AuthKind = JiraAuthKind.BasicEmailApiToken,
            MaxRetries = 3,
            RetryBaseMilliseconds = 0,
        });

        JiraHttpClientConfigurator.Apply(client, opts.Value);

        var svc = new JiraRestIssueService(client, opts, metrics, NullLogger<JiraRestIssueService>.Instance);
        var cmd = new CreateJiraIssueCommand(1, "s", "d");

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CreateIssueAsync(cmd, CancellationToken.None));
        Assert.Equal(1, handler.SendCount);
        Assert.True(metrics.HasRecentAuthFailure(TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public async Task Transient_http_exhausts_retries_on_final_503()
    {
        using var handler = new OperationalSequenceHttpMessageHandler();
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        using var client = new HttpClient(handler, disposeHandler: false);
        using var metrics = new OutboundIntegrationMetrics();

        var opts = Microsoft.Extensions.Options.Options.Create(new JiraOptions
        {
            BaseUrl = "https://example.atlassian.net",
            ProjectKey = "LOG",
            Email = "user@example.com",
            ApiToken = "token",
            AuthKind = JiraAuthKind.BasicEmailApiToken,
            MaxRetries = 2,
            RetryBaseMilliseconds = 0,
        });

        JiraHttpClientConfigurator.Apply(client, opts.Value);

        var svc = new JiraRestIssueService(client, opts, metrics, NullLogger<JiraRestIssueService>.Instance);
        var cmd = new CreateJiraIssueCommand(1, "s", "d");

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CreateIssueAsync(cmd, CancellationToken.None));
        Assert.Equal(2, handler.SendCount);
        Assert.True(metrics.HasRecentRetryExhaustion(TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public async Task Client_timeout_retries_then_exhausts()
    {
        // HttpClient.Timeout does not reliably cancel a custom root HttpMessageHandler's CTS on all platforms;
        // TaskCanceledException without user cancellation matches what SocketsHttpHandler surfaces after a timeout.
        using var handler = new OperationalTaskCanceledHttpMessageHandler();
        using var client = new HttpClient(handler, disposeHandler: false)
        {
            Timeout = TimeSpan.FromSeconds(30),
        };

        using var metrics = new OutboundIntegrationMetrics();

        var opts = Microsoft.Extensions.Options.Options.Create(new JiraOptions
        {
            BaseUrl = "https://example.atlassian.net",
            ProjectKey = "LOG",
            Email = "user@example.com",
            ApiToken = "token",
            AuthKind = JiraAuthKind.BasicEmailApiToken,
            MaxRetries = 2,
            RetryBaseMilliseconds = 0,
        });

        JiraHttpClientConfigurator.Apply(client, opts.Value);

        var svc = new JiraRestIssueService(client, opts, metrics, NullLogger<JiraRestIssueService>.Instance);
        var cmd = new CreateJiraIssueCommand(1, "s", "d");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => svc.CreateIssueAsync(cmd, CancellationToken.None));
        Assert.Equal(2, handler.SendCount);
        Assert.True(metrics.HasRecentRetryExhaustion(TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public async Task Health_check_degrades_when_queue_fill_ratio_high()
    {
        using var metrics = new OutboundIntegrationMetrics();
        var opts = Microsoft.Extensions.Options.Options.Create(new JiraOptions
        {
            EnableIntegration = true,
            QueueCapacity = 10,
            HealthDegradedQueueFillRatio = 0.5,
            UseMockClient = true,
        });

        for (var i = 0; i < 6; i++)
        {
            metrics.RecordEnqueueAccepted();
        }

        var check = new OutboundIntegrationHealthCheck(opts, metrics);
        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("queue", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class OperationalSingleResponseHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public int SendCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SendCount++;
            return Task.FromResult(response);
        }
    }

    private sealed class OperationalSequenceHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _queue = new();

        internal int SendCount { get; private set; }

        internal void Enqueue(HttpResponseMessage response) => _queue.Enqueue(response);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SendCount++;
            return Task.FromResult(_queue.Dequeue());
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                while (_queue.Count > 0)
                {
                    _queue.Dequeue().Dispose();
                }
            }

            base.Dispose(disposing);
        }
    }

    private sealed class OperationalTaskCanceledHttpMessageHandler : HttpMessageHandler
    {
        internal int SendCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SendCount++;
            return Task.FromException<HttpResponseMessage>(
                new TaskCanceledException("Simulates HttpClient-level request timeout."));
        }
    }
}
