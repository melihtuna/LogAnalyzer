using System.Net;
using LogAnalyzer.Infrastructure.Jira;

namespace LogAnalyzer.Infrastructure.Tests;

public sealed class JiraHttpRetryClassifierTests
{
    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public void IsTransient_matches_expected_codes(HttpStatusCode code)
    {
        Assert.True(JiraHttpRetryClassifier.IsTransient(code));
    }

    [Fact]
    public void IsFatalClientError_treats_401_as_fatal()
    {
        Assert.True(JiraHttpRetryClassifier.IsFatalClientError(HttpStatusCode.Unauthorized));
    }

    [Fact]
    public void IsFatalClientError_excludes_429_and_408()
    {
        Assert.False(JiraHttpRetryClassifier.IsFatalClientError(HttpStatusCode.TooManyRequests));
        Assert.False(JiraHttpRetryClassifier.IsFatalClientError(HttpStatusCode.RequestTimeout));
    }

    [Fact]
    public void ClassifyHttp_marks_transient_when_retrying()
    {
        Assert.Equal("transient_http", JiraHttpRetryClassifier.ClassifyHttp(HttpStatusCode.ServiceUnavailable, willRetry: true));
    }

    [Fact]
    public void ClassifyHttp_marks_fatal_when_no_retry()
    {
        Assert.Equal("fatal_client_http", JiraHttpRetryClassifier.ClassifyHttp(HttpStatusCode.BadRequest, willRetry: false));
    }
}
