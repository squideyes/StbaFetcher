using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;

internal sealed class DatabentoBatchApi(HttpClient client, ILogger<DatabentoBatchApi> logger)
{
    public async Task<BatchJob> SubmitJobAsync(BatchSubmitRequest request)
    {
        logger.LogInformation("POST batch.submit_job dataset={Dataset} schema={Schema} symbols={Symbols} window={Start}..{End}",
            request.Dataset, request.Schema, string.Join(",", request.Symbols), request.Start, request.End);
        var sw = Stopwatch.StartNew();
        using var response = await client.PostAsync("batch.submit_job", request.ToFormContent());
        var job = await ReadJsonOrThrowAsync<BatchJob>(response, "batch.submit_job", sw);
        logger.LogInformation("  -> job {JobId} state={State} cost={Cost} records={Records}",
            job.Id, job.State, FormatCost(job.CostUsd), job.RecordCount?.ToString("N0") ?? "?");
        return job;
    }

    public async Task<IReadOnlyList<BatchJob>> ListJobsAsync(string states, string? since = null)
    {
        var query = $"batch.list_jobs?states={Uri.EscapeDataString(states)}";
        if (!string.IsNullOrWhiteSpace(since))
            query += $"&since={Uri.EscapeDataString(since)}";

        logger.LogInformation("GET batch.list_jobs states={States} since={Since}", states, since ?? "(none)");
        var sw = Stopwatch.StartNew();
        using var response = await client.GetAsync(query);
        var jobs = await ReadJsonOrThrowAsync<List<BatchJob>>(response, "batch.list_jobs", sw);
        logger.LogInformation("  -> {Count} job(s) returned", jobs.Count);
        return jobs;
    }

    public async Task<IReadOnlyList<BatchFile>> ListFilesAsync(string jobId)
    {
        logger.LogInformation("GET batch.list_files job_id={JobId}", jobId);
        var sw = Stopwatch.StartNew();
        using var response = await client.GetAsync($"batch.list_files?job_id={Uri.EscapeDataString(jobId)}");
        var files = await ReadJsonOrThrowAsync<List<BatchFile>>(response, "batch.list_files", sw);
        var totalBytes = files.Sum(f => f.Size ?? 0);
        logger.LogInformation("  -> {Count} file(s), {Bytes:N0} bytes total", files.Count, totalBytes);
        return files;
    }

    /// <summary>
    /// POST symbology.resolve — translates one symbol from stype_in to stype_out for the given
    /// dataset/date range. Returns the resolved string for the first interval covering startDate,
    /// or null if the API gave no result.
    /// </summary>
    public async Task<string?> ResolveSymbolAsync(
        string dataset, string symbol, string stypeIn, string stypeOut,
        DateOnly startDate, DateOnly endDateExclusive)
    {
        logger.LogInformation("POST symbology.resolve symbol={Symbol} {In}->{Out} {Start}..{End}",
            symbol, stypeIn, stypeOut, startDate, endDateExclusive);
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

        using var response = await client.PostAsync("symbology.resolve", content);
        var body = await response.Content.ReadAsStringAsync();
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
            {
                var raw = s.GetString();
                logger.LogInformation("  -> resolved {Symbol} -> {Raw} ({Elapsed:F0}ms)", symbol, raw, sw.Elapsed.TotalMilliseconds);
                return raw;
            }
        }
        return null;
    }

    public async Task<long> DownloadFileAsync(string url, string path, long? expectedSize = null)
    {
        var fileName = Path.GetFileName(path);
        logger.LogInformation("GET {File} (expected {Bytes})", fileName, expectedSize?.ToString("N0") ?? "?");
        var sw = Stopwatch.StartNew();

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            logger.LogError("Download failed for {File}: HTTP {Status} {Reason} body={Body}", fileName, (int)response.StatusCode, response.ReasonPhrase, body);
            throw new HttpRequestException($"Download failed {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        }

        var contentLength = response.Content.Headers.ContentLength ?? expectedSize;
        var tmp = path + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var buffer = new byte[81920];
        long totalRead = 0;
        var lastReport = Stopwatch.StartNew();

        await using (var input = await response.Content.ReadAsStreamAsync())
        await using (var output = File.Create(tmp))
        {
            int read;
            while ((read = await input.ReadAsync(buffer)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read));
                totalRead += read;

                if (lastReport.Elapsed.TotalSeconds >= 2.0)
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

        File.Move(tmp, path, overwrite: true);
        var totalMbps = totalRead / 1_048_576.0 / Math.Max(sw.Elapsed.TotalSeconds, 0.001);
        logger.LogInformation("  -> {File} done: {Bytes:N0} bytes in {Elapsed:F2}s ({Speed:F2} MB/s)",
            fileName, totalRead, sw.Elapsed.TotalSeconds, totalMbps);
        return totalRead;
    }

    private async Task<T> ReadJsonOrThrowAsync<T>(HttpResponseMessage response, string operation, Stopwatch sw)
    {
        var body = await response.Content.ReadAsStringAsync();
        sw.Stop();

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("{Operation} failed: HTTP {Status} {Reason} in {Elapsed:F0}ms body={Body}",
                operation, (int)response.StatusCode, response.ReasonPhrase, sw.Elapsed.TotalMilliseconds, body);
            throw new HttpRequestException($"Databento HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        }

        logger.LogDebug("{Operation} -> HTTP {Status} in {Elapsed:F0}ms ({Bytes} bytes)",
            operation, (int)response.StatusCode, sw.Elapsed.TotalMilliseconds, body.Length);

        return JsonSerializer.Deserialize<T>(body, JsonOptions.Indented)
            ?? throw new InvalidOperationException($"Could not deserialize Databento response: {body}");
    }

    private static string FormatCost(decimal? cost)
        => cost.HasValue ? $"${cost.Value:0.####}" : "?";
}
