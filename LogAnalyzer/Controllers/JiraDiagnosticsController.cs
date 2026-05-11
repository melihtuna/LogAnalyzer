using LogAnalyzer.Infrastructure.Jira;
using Microsoft.AspNetCore.Mvc;

namespace LogAnalyzer.Api.Controllers;

[ApiController]
[Route("jira-diagnostics")]
public sealed class JiraDiagnosticsController(
    JiraIssueQueryService jiraIssueQueryService,
    IJiraIssueService jiraIssueService) : ControllerBase
{
    [HttpGet("issues")]
    [ProducesResponseType(typeof(JiraIssueQueryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(object), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(object), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<JiraIssueQueryResponse>> GetIssues(
        [FromQuery] string? jql,
        [FromQuery] int maxResults = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await jiraIssueQueryService
                .QueryIssuesAsync(jql, maxResults, cancellationToken)
                .ConfigureAwait(false);
            return Ok(result);
        }
        catch (JiraIssueQueryException ex) when ((int)ex.StatusCode is 401 or 403)
        {
            return StatusCode((int)ex.StatusCode, new
            {
                error = "jira_auth_failed",
                statusCode = (int)ex.StatusCode,
                details = ex.ResponseBody
            });
        }
        catch (JiraIssueQueryException ex)
        {
            return StatusCode((int)ex.StatusCode, new
            {
                error = "jira_query_failed",
                statusCode = (int)ex.StatusCode,
                details = ex.ResponseBody
            });
        }
    }

    [HttpGet("issues/{issueIdOrKey}")]
    [ProducesResponseType(typeof(JiraIssueDetails), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(object), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(object), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<JiraIssueDetails>> GetIssueByIdOrKey(
        string issueIdOrKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await jiraIssueQueryService.GetIssueAsync(issueIdOrKey, cancellationToken).ConfigureAwait(false);
            return Ok(result);
        }
        catch (JiraIssueQueryException ex) when ((int)ex.StatusCode is 401 or 403)
        {
            return StatusCode((int)ex.StatusCode, new
            {
                error = "jira_auth_failed",
                statusCode = (int)ex.StatusCode,
                details = ex.ResponseBody
            });
        }
        catch (JiraIssueQueryException ex) when ((int)ex.StatusCode == 404)
        {
            return NotFound(new
            {
                error = "jira_issue_not_found",
                statusCode = (int)ex.StatusCode,
                details = ex.ResponseBody
            });
        }
        catch (JiraIssueQueryException ex)
        {
            return StatusCode((int)ex.StatusCode, new
            {
                error = "jira_query_failed",
                statusCode = (int)ex.StatusCode,
                details = ex.ResponseBody
            });
        }
    }

    [HttpPost("issues")]
    [ProducesResponseType(typeof(CreateJiraIssueResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(object), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(object), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<CreateJiraIssueResult>> CreateIssue(
        [FromBody] JiraCreateIssueTestRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Summary) || string.IsNullOrWhiteSpace(request.Description))
        {
            return BadRequest(new
            {
                error = "invalid_request",
                details = "summary and description are required."
            });
        }

        try
        {
            var result = await jiraIssueService.CreateIssueAsync(
                new CreateJiraIssueCommand(request.IncidentId, request.Summary.Trim(), request.Description.Trim()),
                cancellationToken).ConfigureAwait(false);
            return Ok(result);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("HTTP 401", StringComparison.OrdinalIgnoreCase) ||
                                                   ex.Message.Contains("HTTP 403", StringComparison.OrdinalIgnoreCase))
        {
            return Unauthorized(new
            {
                error = "jira_auth_failed",
                details = ex.Message
            });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                error = "jira_create_failed",
                details = ex.Message
            });
        }
    }
}

public sealed record JiraCreateIssueTestRequest(
    int IncidentId,
    string Summary,
    string Description);
