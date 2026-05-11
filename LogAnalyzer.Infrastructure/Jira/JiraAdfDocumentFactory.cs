using System.Text.Json.Nodes;

namespace LogAnalyzer.Infrastructure.Jira;

/// <summary>
/// Builds Atlassian Document Format payloads with stable paragraph ordering for REST v3.
/// </summary>
public static class JiraAdfDocumentFactory
{
    public static JsonObject FromPlainDescription(string plainDescription)
    {
        var content = new JsonArray();

        foreach (var paragraph in SplitParagraphs(plainDescription ?? string.Empty))
        {
            var normalized = NormalizeParagraphText(paragraph);
            content.Add(new JsonObject
            {
                ["type"] = "paragraph",
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = normalized
                    }
                }
            });
        }

        return new JsonObject
        {
            ["type"] = "doc",
            ["version"] = 1,
            ["content"] = content
        };
    }

    private static IEnumerable<string> SplitParagraphs(string plain)
    {
        if (plain.Length == 0)
        {
            yield return string.Empty;
            yield break;
        }

        foreach (var segment in plain.Split("\n\n", StringSplitOptions.None))
        {
            yield return segment;
        }
    }

    private static string NormalizeParagraphText(string paragraph)
    {
        var trimmed = paragraph.Trim();
        return trimmed.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\n', ' ');
    }
}
