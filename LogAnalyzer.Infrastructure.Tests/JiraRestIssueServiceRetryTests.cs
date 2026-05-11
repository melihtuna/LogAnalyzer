using System.Net;
using LogAnalyzer.Infrastructure.Jira;
using LogAnalyzer.Infrastructure.Outbound;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LogAnalyzer.Infrastructure.Tests;

public sealed class JiraRestIssueServiceRetryTests
{
    [Fact]
    public async Task Retries_on_503_then_parses_success_payload()
    {
        using var handler = new SequenceHttpMessageHandler();
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("{\"id\":\"1\",\"key\":\"LOG-99\",\"self\":\"https://x\"}", System.Text.Encoding.UTF8, "application/json"),
        });

        using var client = new HttpClient(handler, disposeHandler: false);

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

        using var metrics = new OutboundIntegrationMetrics();
        var svc = new JiraRestIssueService(client, opts, metrics, NullLogger<JiraRestIssueService>.Instance);
        var cmd = new CreateJiraIssueCommand(5, "sum", "body");

        var result = await svc.CreateIssueAsync(cmd, CancellationToken.None);

        Assert.Equal("LOG-99", result.IssueKey);
        Assert.Equal("https://example.atlassian.net/browse/LOG-99", result.BrowseUrl);
        Assert.Equal(2, handler.SendCount);
    }

    private sealed class SequenceHttpMessageHandler : HttpMessageHandler
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
}
