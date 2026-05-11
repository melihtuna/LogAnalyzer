using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using LogAnalyzer.Domain.Observability;
using LogAnalyzer.Infrastructure.Outbound;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LogAnalyzer.Infrastructure.Jira;

public sealed class JiraRestIssueService(
    HttpClient httpClient,
    IOptions<JiraOptions> options,
    OutboundIntegrationMetrics metrics,
    ILogger<JiraRestIssueService> logger) : IJiraIssueService
{
    private static readonly JsonSerializerOptions ResponseJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<CreateJiraIssueResult> CreateIssueAsync(CreateJiraIssueCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var opts = options.Value;
        ValidateRestOptions(opts);

        var activity = Activity.Current;
        var jsonPayload = JiraIssueCreatePayloadSerializer.Serialize(command, opts);
        var maxAttempts = Math.Max(1, opts.MaxRetries);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            TagAttempt(activity, attempt, maxAttempts);

            using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            content.Headers.ContentType!.CharSet = "utf-8";

            var swHttp = Stopwatch.StartNew();
            try
            {
                using var response = await httpClient
                    .PostAsync("rest/api/3/issue", content, cancellationToken)
                    .ConfigureAwait(false);

                swHttp.Stop();

                metrics.RecordJiraHttpRoundTrip(swHttp.Elapsed.TotalMilliseconds, response.IsSuccessStatusCode, (int)response.StatusCode);

                var status = response.StatusCode;

                if (response.IsSuccessStatusCode)
                {
                    activity?.SetTag(ObservabilityAttributeKeys.JiraHttpStatusCode, (int)status);
                    activity?.SetTag(ObservabilityAttributeKeys.JiraRetryClassification, "success_http");

                    await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    var dto = await JsonSerializer
                        .DeserializeAsync<JiraCreateIssueApiResponseDto>(stream, ResponseJsonOptions, cancellationToken)
                        .ConfigureAwait(false);

                    if (dto?.Key is null || dto.Key.Length == 0)
                    {
                        throw new InvalidOperationException("Jira REST create succeeded but response contained no issue key.");
                    }

                    var browseUrl = $"{opts.BaseUrl.TrimEnd('/')}/browse/{dto.Key}";
                    logger.LogInformation(
                        "Jira REST issue created incident_id={IncidentId} issue_key={IssueKey} attempts_used={Attempt}",
                        command.IncidentId,
                        dto.Key,
                        attempt);

                    return new CreateJiraIssueResult(dto.Key, browseUrl);
                }

                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var willRetry = attempt < maxAttempts && JiraHttpRetryClassifier.IsTransient(status);

                activity?.SetTag(ObservabilityAttributeKeys.JiraHttpStatusCode, (int)status);
                activity?.SetTag(ObservabilityAttributeKeys.JiraRetryClassification, JiraHttpRetryClassifier.ClassifyHttp(status, willRetry));

                var classification = JiraHttpRetryClassifier.ClassifyHttp(status, willRetry);
                metrics.RecordJiraRetryDecision(classification);

                logger.LogWarning(
                    "Jira REST create rejected status={StatusCode} attempt={Attempt} max_attempts={MaxAttempts} incident_id={IncidentId} body_preview={BodyPreview}",
                    (int)status,
                    attempt,
                    maxAttempts,
                    command.IncidentId,
                    Truncate(errorBody, 512));

                if (status == HttpStatusCode.Unauthorized || status == HttpStatusCode.Forbidden)
                {
                    metrics.RecordJiraAuthRejected();
                }

                if (willRetry)
                {
                    await DelayBeforeRetryAsync(opts, attempt, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (JiraHttpRetryClassifier.IsFatalClientError(status))
                {
                    throw new InvalidOperationException(
                        $"Jira REST create failed with fatal HTTP {(int)status}: {Truncate(errorBody, 1024)}");
                }

                if (JiraHttpRetryClassifier.IsTransient(status))
                {
                    metrics.RecordJiraRetryExhausted();
                }

                throw new InvalidOperationException(
                    $"Jira REST create failed with HTTP {(int)status}: {Truncate(errorBody, 1024)}");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                activity?.SetTag(ObservabilityAttributeKeys.JiraRetryClassification, "cancelled");
                throw;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                if (swHttp.IsRunning)
                {
                    swHttp.Stop();
                }

                metrics.RecordJiraHttpRoundTrip(swHttp.Elapsed.TotalMilliseconds, false, 0);

                activity?.SetTag(ObservabilityAttributeKeys.JiraHttpStatusCode, 0);
                activity?.SetTag(
                    ObservabilityAttributeKeys.JiraRetryClassification,
                    attempt < maxAttempts ? "transient_timeout" : "fatal_timeout");

                metrics.RecordJiraRetryDecision(attempt < maxAttempts ? "transient_timeout" : "fatal_timeout");

                logger.LogWarning(
                    ex,
                    "Jira REST create timed out attempt={Attempt} max_attempts={MaxAttempts} incident_id={IncidentId}",
                    attempt,
                    maxAttempts,
                    command.IncidentId);

                if (attempt >= maxAttempts)
                {
                    metrics.RecordJiraRetryExhausted();
                    throw;
                }

                await DelayBeforeRetryAsync(opts, attempt, cancellationToken).ConfigureAwait(false);
                continue;
            }
            catch (HttpRequestException ex)
            {
                if (swHttp.IsRunning)
                {
                    swHttp.Stop();
                }

                metrics.RecordJiraHttpRoundTrip(swHttp.Elapsed.TotalMilliseconds, false, 0);

                activity?.SetTag(ObservabilityAttributeKeys.JiraHttpStatusCode, 0);
                activity?.SetTag(
                    ObservabilityAttributeKeys.JiraRetryClassification,
                    attempt < maxAttempts ? "transient_network" : "fatal_network");

                metrics.RecordJiraRetryDecision(attempt < maxAttempts ? "transient_network" : "fatal_network");

                logger.LogWarning(
                    ex,
                    "Jira REST create transport failure attempt={Attempt} max_attempts={MaxAttempts} incident_id={IncidentId}",
                    attempt,
                    maxAttempts,
                    command.IncidentId);

                if (attempt >= maxAttempts)
                {
                    metrics.RecordJiraRetryExhausted();
                    throw;
                }

                await DelayBeforeRetryAsync(opts, attempt, cancellationToken).ConfigureAwait(false);
                continue;
            }
        }

        metrics.RecordJiraRetryExhausted();
        throw new InvalidOperationException("Jira REST create retries exhausted without outcome.");
    }

    private static void ValidateRestOptions(JiraOptions opts)
    {
        if (string.IsNullOrWhiteSpace(opts.BaseUrl))
        {
            throw new InvalidOperationException("Jira:BaseUrl must be configured for REST mode.");
        }

        if (string.IsNullOrWhiteSpace(opts.ProjectKey))
        {
            throw new InvalidOperationException("Jira:ProjectKey must be configured for REST mode.");
        }

        switch (opts.AuthKind)
        {
            case JiraAuthKind.BearerPat:
                if (string.IsNullOrWhiteSpace(opts.PersonalAccessToken))
                {
                    throw new InvalidOperationException("Jira:PersonalAccessToken must be configured when AuthKind is BearerPat.");
                }

                break;
            default:
                if (string.IsNullOrWhiteSpace(opts.Email) || string.IsNullOrWhiteSpace(opts.ApiToken))
                {
                    throw new InvalidOperationException(
                        "Jira:Email and Jira:ApiToken must be configured when AuthKind is BasicEmailApiToken.");
                }

                break;
        }
    }

    private static void TagAttempt(Activity? activity, int attempt, int maxAttempts)
    {
        activity?.SetTag(ObservabilityAttributeKeys.JiraRetryAttempt, attempt);
        activity?.SetTag(ObservabilityAttributeKeys.JiraRetryMaxAttempts, maxAttempts);
    }

    private static async Task DelayBeforeRetryAsync(JiraOptions opts, int failedAttemptIndex, CancellationToken cancellationToken)
    {
        var delay = JiraOutboundRetryTiming.ComputeDelay(opts, failedAttemptIndex);
        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
    }

    private static string Truncate(string value, int maxChars)
    {
        if (value.Length <= maxChars)
        {
            return value;
        }

        return string.Concat(value.AsSpan(0, maxChars), "...");
    }

    private sealed class JiraCreateIssueApiResponseDto
    {
        public string? Id { get; init; }

        public string? Key { get; init; }

        public string? Self { get; init; }
    }
}
