using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Net.Http.Headers;
using System.Text.Json;
using LogAnalyzer.Domain.Interfaces;
using LogAnalyzer.Infrastructure.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LogAnalyzer.Infrastructure.Services;

public class GraylogLogProvider(
    HttpClient httpClient,
    IOptions<GraylogOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<GraylogLogProvider> logger) : ILogProvider
{
    private const string CheckpointSource = "graylog";
    private readonly object sync = new();
    private DateTimeOffset? lastProcessedTimestampUtc;
    private bool checkpointLoaded;

    public async Task<string> GetLogsAsync()
    {
        await EnsureCheckpointLoadedAsync();

        var configuration = options.Value;
        if (string.IsNullOrWhiteSpace(configuration.BaseUrl))
        {
            logger.LogWarning("Graylog base URL is not configured. Skipping fetch.");
            return string.Empty;
        }

        var rangeMinutes = Math.Max(configuration.TimeRangeMinutes, 1);
        var pageSize = Math.Clamp(configuration.PageSize, 100, 500);
        var maxLogsPerCycle = Math.Max(configuration.MaxLogsPerCycle, pageSize);
        var timeout = TimeSpan.FromSeconds(Math.Clamp(configuration.RequestTimeoutSeconds, 10, 30));
        var maxRetries = Math.Clamp(configuration.MaxRetries, 1, 3);
        var retryDelay = TimeSpan.FromMilliseconds(Math.Max(configuration.RetryDelayMilliseconds, 100));
        var lastTimestamp = GetLastProcessedTimestampUtc();
        if (lastTimestamp.HasValue)
        {
            var sinceMinutes = (int)Math.Ceiling((DateTimeOffset.UtcNow - lastTimestamp.Value).TotalMinutes);
            rangeMinutes = Math.Max(rangeMinutes, sinceMinutes + 1);
        }

        var lines = new List<string>();
        var newestTimestamp = lastTimestamp;
        var seenHashes = new HashSet<string>(StringComparer.Ordinal);
        var offset = 0;

        while (lines.Count < maxLogsPerCycle)
        {
            var page = await FetchPageWithRetryAsync(
                configuration,
                rangeMinutes,
                pageSize,
                offset,
                timeout,
                maxRetries,
                retryDelay);

            if (page is null || page.Count == 0)
            {
                break;
            }

            logger.LogInformation("Graylog page fetched with {Count} messages at offset {Offset}.", page.Count, offset);
            foreach (var messageObject in page)
            {
                var timestamp = GetTimestamp(messageObject);
                if (!timestamp.HasValue)
                {
                    continue;
                }

                if (lastTimestamp.HasValue && timestamp.Value <= lastTimestamp.Value)
                {
                    continue;
                }

                var level = TryGetString(messageObject, "level");
                var message = TryGetMessage(messageObject);
                if (string.IsNullOrWhiteSpace(message))
                {
                    continue;
                }

                var dedupeHash = ComputeHash(timestamp.Value, level, message);
                if (!seenHashes.Add(dedupeHash))
                {
                    logger.LogDebug("Graylog duplicate skipped for timestamp {Timestamp}.", timestamp.Value);
                    continue;
                }

                lines.Add(FormatLogLine(timestamp.Value, level, message));
                if (!newestTimestamp.HasValue || timestamp.Value > newestTimestamp.Value)
                {
                    newestTimestamp = timestamp.Value;
                }

                if (lines.Count >= maxLogsPerCycle)
                {
                    logger.LogWarning(
                        "Graylog fetch truncated at max logs per cycle ({MaxLogsPerCycle}).",
                        maxLogsPerCycle);
                    break;
                }
            }

            if (page.Count < pageSize)
            {
                break;
            }

            offset += pageSize;
        }

        if (lines.Count > 0 && newestTimestamp.HasValue)
        {
            SetLastProcessedTimestampUtc(newestTimestamp.Value);
            await PersistCheckpointAsync(newestTimestamp.Value);
        }
        else
        {
            logger.LogInformation("Graylog fetch returned no new log lines after checkpoint filtering.");
        }

        return string.Join(Environment.NewLine, lines.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private async Task EnsureCheckpointLoadedAsync()
    {
        var needsLoad = false;
        lock (sync)
        {
            if (!checkpointLoaded)
            {
                checkpointLoaded = true;
                needsLoad = true;
            }
        }

        if (!needsLoad)
        {
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<ILogSourceCheckpointRepository>();
            var checkpoint = await repository.GetBySourceAsync(CheckpointSource);
            if (checkpoint is not null)
            {
                SetLastProcessedTimestampUtc(checkpoint.LastProcessedTimestampUtc);
                logger.LogInformation("Graylog checkpoint loaded: {Timestamp}.", checkpoint.LastProcessedTimestampUtc);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load Graylog checkpoint from database.");
        }
    }

    private async Task PersistCheckpointAsync(DateTimeOffset timestampUtc)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<ILogSourceCheckpointRepository>();
            await repository.UpsertAsync(CheckpointSource, timestampUtc);
            await repository.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist Graylog checkpoint.");
        }
    }

    private async Task<List<JsonElement>?> FetchPageWithRetryAsync(
        GraylogOptions configuration,
        int rangeMinutes,
        int pageSize,
        int offset,
        TimeSpan timeout,
        int maxRetries,
        TimeSpan retryDelay)
    {
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var requestUri = BuildSearchUri(configuration.BaseUrl, configuration.Query, rangeMinutes, pageSize, offset);
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                if (!string.IsNullOrWhiteSpace(configuration.ApiToken))
                {
                    var credentials = $"{configuration.ApiToken}:token";
                    var encodedCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials));
                    request.Headers.Authorization = new AuthenticationHeaderValue("Basic", encodedCredentials);
                }

                using var timeoutCts = new CancellationTokenSource(timeout);
                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning(
                        "Graylog request failed with status {StatusCode} on attempt {Attempt}/{MaxRetries}.",
                        (int)response.StatusCode,
                        attempt,
                        maxRetries);
                }
                else
                {
                    var responseBody = await response.Content.ReadAsStringAsync(timeoutCts.Token);
                    using var json = JsonDocument.Parse(responseBody);
                    if (!json.RootElement.TryGetProperty("messages", out var messagesElement) ||
                        messagesElement.ValueKind != JsonValueKind.Array)
                    {
                        logger.LogWarning("Graylog response does not contain a valid messages array.");
                        return [];
                    }

                    return messagesElement
                        .EnumerateArray()
                        .Where(x => x.TryGetProperty("message", out _))
                        .Select(x => x.GetProperty("message").Clone())
                        .ToList();
                }
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning(
                    "Graylog request timed out on attempt {Attempt}/{MaxRetries}.",
                    attempt,
                    maxRetries);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Graylog response deserialization failed on attempt {Attempt}/{MaxRetries}.", attempt, maxRetries);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Graylog request failed on attempt {Attempt}/{MaxRetries}.", attempt, maxRetries);
            }

            if (attempt < maxRetries)
            {
                logger.LogInformation(
                    "Retrying Graylog request in {DelayMs}ms (attempt {NextAttempt}/{MaxRetries}).",
                    (int)retryDelay.TotalMilliseconds,
                    attempt + 1,
                    maxRetries);
                await Task.Delay(retryDelay);
            }
        }

        logger.LogWarning("Graylog page fetch failed after {MaxRetries} attempts. Returning empty page.", maxRetries);
        return [];
    }

    private static string BuildSearchUri(string baseUrl, string query, int rangeMinutes, int limit, int offset)
    {
        var normalizedBaseUrl = baseUrl.TrimEnd('/');
        var encodedQuery = Uri.EscapeDataString(string.IsNullOrWhiteSpace(query) ? "*" : query);
        var rangeSeconds = checked(rangeMinutes * 60);
        return $"{normalizedBaseUrl}/api/search/universal/relative?query={encodedQuery}&range={rangeSeconds}&limit={limit}&offset={offset}&sort=timestamp:asc";
    }

    private static DateTimeOffset? GetTimestamp(JsonElement messageObject)
    {
        if (!messageObject.TryGetProperty("timestamp", out var timestampElement))
        {
            return null;
        }

        var rawTimestamp = timestampElement.GetString();
        if (string.IsNullOrWhiteSpace(rawTimestamp))
        {
            return null;
        }

        return DateTimeOffset.TryParse(rawTimestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }

    private static string TryGetMessage(JsonElement messageObject)
    {
        var message = TryGetString(messageObject, "full_message");
        if (string.IsNullOrWhiteSpace(message))
        {
            message = TryGetString(messageObject, "short_message");
        }
        if (string.IsNullOrWhiteSpace(message))
        {
            message = TryGetString(messageObject, "message");
        }

        return message.Trim();
    }

    private static string FormatLogLine(DateTimeOffset timestamp, string level, string message)
    {
        var normalizedLevel = string.IsNullOrWhiteSpace(level) ? "UNKNOWN" : level.Trim();
        return $"[{timestamp:O}] [{normalizedLevel}] {message.Trim()}";
    }

    private static string TryGetString(JsonElement messageObject, string propertyName)
    {
        if (!messageObject.TryGetProperty(propertyName, out var property))
        {
            return string.Empty;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString() ?? string.Empty,
            JsonValueKind.Number => property.GetRawText(),
            _ => string.Empty
        };
    }

    private DateTimeOffset? GetLastProcessedTimestampUtc()
    {
        lock (sync)
        {
            return lastProcessedTimestampUtc;
        }
    }

    private void SetLastProcessedTimestampUtc(DateTimeOffset timestampUtc)
    {
        lock (sync)
        {
            lastProcessedTimestampUtc = timestampUtc;
        }
    }

    private static string ComputeHash(DateTimeOffset timestamp, string level, string message)
    {
        var raw = $"{timestamp:O}|{level}|{message}";
        var bytes = Encoding.UTF8.GetBytes(raw);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}

