using LogAnalyzer.Infrastructure.Jira;

namespace LogAnalyzer.Infrastructure.Tests;

public sealed class JiraIssueCreatePayloadSerializerTests
{
    [Fact]
    public void Serialized_fields_follow_stable_wire_order()
    {
        var cmd = new CreateJiraIssueCommand(11, "Hello", "Incident metadata:\n\nSummary:\none");
        var opts = new JiraOptions
        {
            ProjectKey = "MYPROJ",
            IssueTypeName = "Bug",
            BaseUrl = "https://site.example.net",
            Email = "x@y.z",
            ApiToken = "secret",
        };

        var json = JiraIssueCreatePayloadSerializer.Serialize(cmd, opts);

        var iProject = json.IndexOf("\"project\"", StringComparison.Ordinal);
        var iIssuetype = json.IndexOf("\"issuetype\"", StringComparison.Ordinal);
        var iSummary = json.IndexOf("\"summary\"", StringComparison.Ordinal);
        var iDescription = json.IndexOf("\"description\"", StringComparison.Ordinal);

        Assert.True(iProject >= 0 && iIssuetype >= 0 && iSummary >= 0 && iDescription >= 0);
        Assert.True(iProject < iIssuetype && iIssuetype < iSummary && iSummary < iDescription);

        Assert.Contains("\"key\":\"MYPROJ\"", json);
        Assert.Contains("\"name\":\"Bug\"", json);
        Assert.Contains("\"type\":\"doc\"", json);
        Assert.Contains("\"Hello\"", json);
    }
}
