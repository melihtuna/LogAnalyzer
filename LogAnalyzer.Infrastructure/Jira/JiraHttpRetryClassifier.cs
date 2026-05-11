using System.Net;

namespace LogAnalyzer.Infrastructure.Jira;

internal static class JiraHttpRetryClassifier
{
    internal static bool IsTransient(HttpStatusCode code) =>
        code switch
        {
            HttpStatusCode.RequestTimeout => true,
            HttpStatusCode.TooManyRequests => true,
            HttpStatusCode.BadGateway => true,
            HttpStatusCode.ServiceUnavailable => true,
            HttpStatusCode.GatewayTimeout => true,
            _ => false,
        };

    internal static bool IsFatalClientError(HttpStatusCode code)
    {
        var v = (int)code;
        return v is >= 400 and < 500
               && code != HttpStatusCode.TooManyRequests
               && code != HttpStatusCode.RequestTimeout;
    }

    internal static string ClassifyHttp(HttpStatusCode code, bool willRetry)
    {
        if ((int)code < 400)
        {
            return "success_http";
        }

        if (willRetry && IsTransient(code))
        {
            return "transient_http";
        }

        if (IsFatalClientError(code))
        {
            return "fatal_client_http";
        }

        return "non_retryable_http";
    }
}
