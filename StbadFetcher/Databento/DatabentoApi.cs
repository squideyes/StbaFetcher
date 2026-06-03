using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace StbadFetcher;

/// <summary>
/// Thin wrapper over Databento's synchronous historical HTTP API. Two operations:
/// <list type="bullet">
///   <item><see cref="StreamGetRangeAsync"/> — POST <c>v0/timeseries.get_range</c>, streaming
///         the DBN response body straight to a local file.</item>
///   <item><see cref="ResolveSymbolAsync"/> — POST <c>v0/symbology.resolve</c>, used as the
///         <c>instrument_id → raw_symbol</c> fallback after parsing DBN metadata
///         (GLBX.MDP3 rejects <c>continuous → raw_symbol</c> directly).</item>
/// </list>
/// </summary>
internal sealed class DatabentoApi(HttpClient client, ILogger<DatabentoApi> logger)
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan[] BackoffSchedule = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)];

    /// <summary>
    /// Streams <c>timeseries.get_range</c> for one symbol over <c>[startUtc, endUtc]</c> straight
    /// to <paramref name="destPath"/>. Writes to a <c>.tmp</c> sibling first and atomically renames
    /// on success, so a cancellation or crash mid-stream leaves nothing the resume logic could
    /// mistake for a complete file. Retries on 408/429/5xx with backoff (honoring
    /// <c>Retry-After</c>); non-transient HTTP failures throw immediately.
    /// </summary>
    public async Task<long> StreamGetRangeAsync(
        string dataset, string symbol, string schema,
        DateTimeOffset startUtc, DateTimeOffset endUtc,
        string stypeIn, string stypeOut,
        string destPath, CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(destPath);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await StreamOnceAsync(
                    dataset, symbol, schema, startUtc, endUtc, stypeIn, stypeOut, destPath, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (attempt < MaxAttempts && (ex.StatusCode is null || IsTransient(ex.StatusCode)))
            {
                // StatusCode == null means a connection-level failure (DNS, TCP reset, TLS,
                // socket timeout, etc.) — those are exactly what we want to retry. A specific
                // 4xx/5xx is honored via IsTransient. Anything else (e.g. 401, 422) escapes.
                var wait = ex.Data["RetryAfter"] as TimeSpan? ?? BackoffSchedule[attempt - 1];
                var label = ex.StatusCode is null ? $"network ({ex.Message})" : ex.StatusCode.ToString()!;
                logger.LogWarning(
                    "  {File}: attempt {Attempt}/{Max} failed ({Label}); retrying in {Wait:F1}s.",
                    fileName, attempt, MaxAttempts, label, wait.TotalSeconds);
                await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException ex) when (attempt < MaxAttempts)
            {
                var wait = BackoffSchedule[attempt - 1];
                logger.LogWarning(
                    "  {File}: attempt {Attempt}/{Max} IO error ({Msg}); retrying in {Wait:F1}s.",
                    fileName, attempt, MaxAttempts, ex.Message, wait.TotalSeconds);
                await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException ex) when (attempt < MaxAttempts && !cancellationToken.IsCancellationRequested)
            {
                // HttpClient surfaces request timeouts as TaskCanceledException (not the
                // caller's cancel). Retry these; let real user-initiated cancels propagate.
                var wait = BackoffSchedule[attempt - 1];
                logger.LogWarning(
                    "  {File}: attempt {Attempt}/{Max} timed out ({Msg}); retrying in {Wait:F1}s.",
                    fileName, attempt, MaxAttempts, ex.Message, wait.TotalSeconds);
                await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<long> StreamOnceAsync(
        string dataset, string symbol, string schema,
        DateTimeOffset startUtc, DateTimeOffset endUtc,
        string stypeIn, string stypeOut,
        string destPath, CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(destPath);
        var sw = Stopwatch.StartNew();

        var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("dataset", dataset),
            new KeyValuePair<string, string>("symbols", symbol),
            new KeyValuePair<string, string>("schema", schema),
            new KeyValuePair<string, string>("start", FormatTimestamp(startUtc)),
            new KeyValuePair<string, string>("end", FormatTimestamp(endUtc)),
            new KeyValuePair<string, string>("encoding", "dbn"),
            new KeyValuePair<string, string>("compression", "zstd"),
            new KeyValuePair<string, string>("stype_in", stypeIn),
            new KeyValuePair<string, string>("stype_out", stypeOut),
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "timeseries.get_range") { Content = form };
        logger.LogInformation("GET-range {File} ({Sym} {Start:HH:mm}..{End:HH:mm} UTC)",
            fileName, symbol, startUtc, endUtc);

        using var response = await client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var snippet = body.Length > 500 ? body[..500] + "…" : body;
            var ex = new HttpRequestException(
                $"timeseries.get_range failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {snippet}",
                inner: null, statusCode: response.StatusCode);
            ex.Data["RetryAfter"] = ParseRetryAfter(response);
            logger.LogError("  {File}: HTTP {Status} body={Body}", fileName, (int)response.StatusCode, snippet);
            throw ex;
        }

        if (response.StatusCode == HttpStatusCode.PartialContent)
            logger.LogWarning("  {File}: 206 Partial Content — one or more symbols could not be resolved.",
                fileName);

        var contentLength = response.Content.Headers.ContentLength;
        var tmp = destPath + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

        var buffer = new byte[81920];
        long totalRead = 0;
        var lastReport = Stopwatch.StartNew();

        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var output = File.Create(tmp))
        {
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                totalRead += read;

                if (lastReport.Elapsed.TotalSeconds >= 5.0)
                {
                    var mbps = totalRead / 1_048_576.0 / sw.Elapsed.TotalSeconds;
                    if (contentLength is long expected and > 0)
                    {
                        var pct = totalRead * 100.0 / expected;
                        logger.LogInformation("  {File} {Pct:F1}% ({Done:N0}/{Total:N0} bytes @ {Speed:F2} MB/s)",
                            fileName, pct, totalRead, expected, mbps);
                    }
                    else
                    {
                        logger.LogInformation("  {File} {Done:N0} bytes @ {Speed:F2} MB/s", fileName, totalRead, mbps);
                    }
                    lastReport.Restart();
                }
            }
        }

        File.Move(tmp, destPath, overwrite: true);
        var totalMbps = totalRead / 1_048_576.0 / Math.Max(sw.Elapsed.TotalSeconds, 0.001);
        logger.LogInformation("  -> {File} done: {Bytes:N0} bytes in {Elapsed:F2}s ({Speed:F2} MB/s)",
            fileName, totalRead, sw.Elapsed.TotalSeconds, totalMbps);
        return totalRead;
    }

    /// <summary>
    /// POST symbology.resolve — translates one symbol from stype_in to stype_out for the given
    /// dataset/date range. Returns the resolved string for the first interval covering startDate,
    /// or null if the API gave no result.
    /// </summary>
    public async Task<string?> ResolveSymbolAsync(
        string dataset, string symbol, string stypeIn, string stypeOut,
        DateOnly startDate, DateOnly endDateExclusive, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("dataset", dataset),
            new KeyValuePair<string, string>("symbols", symbol),
            new KeyValuePair<string, string>("stype_in", stypeIn),
            new KeyValuePair<string, string>("stype_out", stypeOut),
            new KeyValuePair<string, string>("start_date", startDate.ToString("yyyy-MM-dd")),
            new KeyValuePair<string, string>("end_date", endDateExclusive.ToString("yyyy-MM-dd"))
        });

        using var response = await client.PostAsync("symbology.resolve", content, cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        sw.Stop();

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("symbology.resolve failed: HTTP {Status} body={Body}", (int)response.StatusCode, body);
            return null;
        }

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("result", out var result)) return null;
        if (!result.TryGetProperty(symbol, out var mappings)) return null;
        foreach (var interval in mappings.EnumerateArray())
        {
            if (interval.TryGetProperty("s", out var s))
                return s.GetString();
        }
        return null;
    }

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static bool IsTransient(HttpStatusCode? status) => status is
        HttpStatusCode.RequestTimeout            // 408
        or HttpStatusCode.TooManyRequests        // 429
        or HttpStatusCode.InternalServerError    // 500
        or HttpStatusCode.BadGateway             // 502
        or HttpStatusCode.ServiceUnavailable     // 503
        or HttpStatusCode.GatewayTimeout;        // 504

    private static TimeSpan? ParseRetryAfter(HttpResponseMessage response)
    {
        var ra = response.Headers.RetryAfter;
        return ra switch
        {
            { Delta: { } d } when d > TimeSpan.Zero => d,
            { Date: { } when_ } => (when_ - DateTimeOffset.UtcNow) is { } d && d > TimeSpan.Zero ? d : TimeSpan.Zero,
            _ => null,
        };
    }
}
