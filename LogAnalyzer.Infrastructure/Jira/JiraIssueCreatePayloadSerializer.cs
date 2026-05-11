using System.Text.Json;
using System.Text.Json.Serialization;

namespace LogAnalyzer.Infrastructure.Jira;

/// <summary>
/// Stable REST v3 create-issue JSON projection shared by <see cref="JiraRestIssueService"/> and tests.
/// </summary>
public static class JiraIssueCreatePayloadSerializer
{
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static string Serialize(CreateJiraIssueCommand command, JiraOptions options)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(options);

        var projectKey = options.ProjectKey.Trim();
        var issueTypeName = string.IsNullOrWhiteSpace(options.IssueTypeName)
            ? "Task"
            : options.IssueTypeName.Trim();

        var adf = JiraAdfDocumentFactory.FromPlainDescription(command.Description);
        var descriptionElement = JsonSerializer.SerializeToElement(adf, SerializerOptions);

        var body = new JiraCreateIssueApiRequestDto
        {
            Fields = new JiraCreateIssueFieldsDto
            {
                Project = new JiraProjectRefDto { Key = projectKey },
                Issuetype = new JiraIssueTypeRefDto { Name = issueTypeName },
                Summary = command.Summary,
                Description = descriptionElement,
            },
        };

        return JsonSerializer.Serialize(body, SerializerOptions);
    }

    private sealed class JiraCreateIssueApiRequestDto
    {
        public JiraCreateIssueFieldsDto Fields { get; init; } = null!;
    }

    private sealed class JiraCreateIssueFieldsDto
    {
        [JsonPropertyOrder(1)]
        public JiraProjectRefDto Project { get; init; } = null!;

        [JsonPropertyOrder(2)]
        public JiraIssueTypeRefDto Issuetype { get; init; } = null!;

        [JsonPropertyOrder(3)]
        public string Summary { get; init; } = "";

        [JsonPropertyOrder(4)]
        public JsonElement Description { get; init; }
    }

    private sealed class JiraProjectRefDto
    {
        public string Key { get; init; } = "";
    }

    private sealed class JiraIssueTypeRefDto
    {
        public string Name { get; init; } = "";
    }
}
