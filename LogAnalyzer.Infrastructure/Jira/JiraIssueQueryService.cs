using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace LogAnalyzer.Infrastructure.Jira;

public sealed class JiraIssueQueryService(
    HttpClient httpClient,
    IOptions<JiraOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<JiraIssueQueryResponse> QueryIssuesAsync(
        string? jql,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.BaseUrl))
        {
            throw new InvalidOperationException("Jira:BaseUrl must be configured.");
        }

        var safeJql = string.IsNullOrWhiteSpace(jql)
            ? $"project = {opts.ProjectKey} ORDER BY created DESC"
            : jql.Trim();
        var safeMaxResults = Math.Clamp(maxResults, 1, 100);

        var uri = $"rest/api/3/search/jql?jql={Uri.EscapeDataString(safeJql)}&maxResults={safeMaxResults}";
        using var response = await httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new JiraIssueQueryException(response.StatusCode, body);
        }

        var dto = JsonSerializer.Deserialize<JiraSearchResponseDto>(body, JsonOptions)
                  ?? new JiraSearchResponseDto();

        var issues = dto.Issues
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .Select(x => new JiraIssueSummary(
                x.Key!,
                x.Fields?.Summary ?? string.Empty,
                x.Fields?.Status?.Name ?? string.Empty,
                x.Fields?.IssueType?.Name ?? string.Empty,
                x.Fields?.Assignee?.DisplayName ?? string.Empty,
                x.Self ?? string.Empty))
            .ToList();

        return new JiraIssueQueryResponse(
            dto.Total ?? issues.Count,
            dto.MaxResults ?? safeMaxResults,
            issues);
    }

    public async Task<JiraIssueDetails> GetIssueAsync(
        string issueIdOrKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(issueIdOrKey))
        {
            throw new ArgumentException("Issue id or key is required.", nameof(issueIdOrKey));
        }

        var uri = $"rest/api/3/issue/{Uri.EscapeDataString(issueIdOrKey.Trim())}";
        using var response = await httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new JiraIssueQueryException(response.StatusCode, body);
        }

        var dto = JsonSerializer.Deserialize<JiraIssueDto>(body, JsonOptions) ?? new JiraIssueDto();
        return new JiraIssueDetails(
            dto.Id ?? string.Empty,
            dto.Key ?? string.Empty,
            dto.Fields?.Summary ?? string.Empty,
            dto.Fields?.Status?.Name ?? string.Empty,
            dto.Fields?.IssueType?.Name ?? string.Empty,
            dto.Fields?.Assignee?.DisplayName ?? string.Empty,
            dto.Self ?? string.Empty);
    }

    private sealed class JiraSearchResponseDto
    {
        public int? Total { get; init; }

        public int? MaxResults { get; init; }

        public List<JiraIssueDto> Issues { get; init; } = [];
    }

    private sealed class JiraIssueDto
    {
        public string? Id { get; init; }

        public string? Key { get; init; }

        public string? Self { get; init; }

        public JiraIssueFieldsDto? Fields { get; init; }
    }

    private sealed class JiraIssueFieldsDto
    {
        public string? Summary { get; init; }

        public JiraStatusDto? Status { get; init; }

        public JiraIssueTypeDto? IssueType { get; init; }

        public JiraUserDto? Assignee { get; init; }
    }

    private sealed class JiraStatusDto
    {
        public string? Name { get; init; }
    }

    private sealed class JiraIssueTypeDto
    {
        public string? Name { get; init; }
    }

    private sealed class JiraUserDto
    {
        public string? DisplayName { get; init; }
    }
}

public sealed record JiraIssueQueryResponse(
    int Total,
    int MaxResults,
    IReadOnlyList<JiraIssueSummary> Issues);

public sealed record JiraIssueSummary(
    string Key,
    string Summary,
    string Status,
    string IssueType,
    string Assignee,
    string SelfUrl);

public sealed record JiraIssueDetails(
    string Id,
    string Key,
    string Summary,
    string Status,
    string IssueType,
    string Assignee,
    string SelfUrl);

public sealed class JiraIssueQueryException(HttpStatusCode statusCode, string responseBody)
    : Exception($"Jira query failed with HTTP {(int)statusCode}.")
{
    public HttpStatusCode StatusCode { get; } = statusCode;

    public string ResponseBody { get; } = responseBody;
}
