using System.Net.Http.Headers;
using System.Text;

namespace LogAnalyzer.Infrastructure.Jira;

internal static class JiraHttpClientConfigurator
{
    internal static void Apply(HttpClient client, JiraOptions options)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);

        var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl)
            ? "https://invalid/"
            : options.BaseUrl.Trim().TrimEnd('/');

        client.BaseAddress = new Uri(baseUrl + "/", UriKind.Absolute);
        client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.RequestTimeoutSeconds, 5, 180));

        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");

        client.DefaultRequestHeaders.Authorization = BuildAuthorization(options);
    }

    private static AuthenticationHeaderValue? BuildAuthorization(JiraOptions options)
    {
        return options.AuthKind switch
        {
            JiraAuthKind.BearerPat =>
                string.IsNullOrWhiteSpace(options.PersonalAccessToken)
                    ? null
                    : new AuthenticationHeaderValue("Bearer", options.PersonalAccessToken.Trim()),
            _ =>
                string.IsNullOrWhiteSpace(options.Email) || string.IsNullOrWhiteSpace(options.ApiToken)
                    ? null
                    : new AuthenticationHeaderValue(
                        "Basic",
                        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Email.Trim()}:{options.ApiToken.Trim()}")))
        };
    }
}
