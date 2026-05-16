using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

// === Devtest config (edit here) =====================================
var dataset = "GLBX.MDP3";
var schema = "mbp-1";
string[] symbols = ["ES.FUT", "NQ.FUT"];
var stypeIn = "parent";
var stypeOut = "instrument_id";
var encoding = "dbn";
var compression = "zstd";
var splitDuration = "day";
var splitSymbols = true;
var outputRoot = @"Z:\DataBento\Raw";
var startDate = new DateOnly(2025, 1, 2);
var endDateExclusive = new DateOnly(2025, 1, 7);  // Jan 2, 3, 6 = 3 weekdays
var startEt = new TimeOnly(8, 0);
var endEt = new TimeOnly(12, 0);
var weekdaysOnly = true;
var pollSeconds = 15;
var maxDownloadConcurrency = 4;
var verifySha256 = true;
// =====================================================================

AppLogging.Init(LogLevel.Information);
var logger = AppLogging.Factory.CreateLogger("app");

var config = new ConfigurationBuilder()
    .AddUserSecrets(typeof(Program).Assembly, optional: true)
    .AddEnvironmentVariables()
    .Build();

var apiKey = config["DATABENTO_API_KEY"]
    ?? throw new InvalidOperationException("Set DATABENTO_API_KEY via `dotnet user-secrets set DATABENTO_API_KEY db-...` or environment variable.");

logger.LogInformation("Dataset/Schema:  {Dataset} / {Schema}", dataset, schema);
logger.LogInformation("Symbols:         {Symbols} (stype_in={In}, stype_out={Out})", string.Join(", ", symbols), stypeIn, stypeOut);
logger.LogInformation("ET window:       {Start} - {End}", startEt, endEt);
logger.LogInformation("Date range:      {Start:yyyy-MM-dd} through {End:yyyy-MM-dd}", startDate, endDateExclusive.AddDays(-1));
logger.LogInformation("Output root:     {Root}", outputRoot);
logger.LogInformation("Poll interval:   {Seconds}s   concurrency={Conc}   verify_sha256={Verify}", pollSeconds, maxDownloadConcurrency, verifySha256);

Directory.CreateDirectory(outputRoot);

var requests = new List<BatchSubmitRequest>();
for (var d = startDate; d < endDateExclusive; d = d.AddDays(1))
{
    if (weekdaysOnly && d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        continue;
    var (startUtc, endUtc) = EasternTradingWindow.ToUtc(d, startEt, endEt);
    requests.Add(new BatchSubmitRequest(
        Dataset: dataset,
        Symbols: symbols,
        Schema: schema,
        Start: startUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
        End: endUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
        Encoding: encoding,
        Compression: compression,
        SplitDuration: splitDuration,
        SplitSymbols: splitSymbols,
        STypeIn: stypeIn,
        STypeOut: stypeOut,
        LocalDate: d));
}

logger.LogInformation("Built {Count} request(s)", requests.Count);
foreach (var r in requests)
    logger.LogInformation("  {Date}: {Start} -> {End} UTC", r.LocalDate, r.Start, r.End);

using var client = DatabentoHttpClient.Create(apiKey);
var api = new DatabentoBatchApi(client, AppLogging.CreateLogger<DatabentoBatchApi>());

var submittedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
decimal estCost = 0m;

for (var i = 0; i < requests.Count; i++)
{
    var r = requests[i];
    logger.LogInformation("[{N}/{T}] SUBMIT {Date} {Start} -> {End}", i + 1, requests.Count, r.LocalDate, r.Start, r.End);
    var job = await api.SubmitJobAsync(r);
    submittedIds.Add(job.Id);
    if (job.CostUsd.HasValue) estCost += job.CostUsd.Value;
    logger.LogInformation("[{N}/{T}] queued {JobId} state={State} cost={Cost}",
        i + 1, requests.Count, job.Id, job.State, job.CostUsd?.ToString("0.####", CultureInfo.InvariantCulture) ?? "?");
    if (i < requests.Count - 1)
        await Task.Delay(TimeSpan.FromSeconds(4));
}

logger.LogInformation("Submission complete: {Count} job(s), est_cost=${Cost:0.####}", submittedIds.Count, estCost);

var downloaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
var sinceFilter = startDate.AddDays(-7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
var pollStart = Stopwatch.StartNew();
var cycle = 0;
var prevState = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

while (downloaded.Count < submittedIds.Count)
{
    cycle++;
    logger.LogInformation("=== Poll cycle #{Cycle} (elapsed {Elapsed:F0}s, {Done}/{Total} downloaded) ===",
        cycle, pollStart.Elapsed.TotalSeconds, downloaded.Count, submittedIds.Count);

    var allJobs = await api.ListJobsAsync("queued,processing,done", since: sinceFilter);
    var ours = allJobs.Where(j => submittedIds.Contains(j.Id)).ToList();

    foreach (var j in ours)
    {
        var prev = prevState.GetValueOrDefault(j.Id, "(new)");
        if (!string.Equals(prev, j.State, StringComparison.OrdinalIgnoreCase))
            logger.LogInformation("  {Id}: {Prev} -> {New} progress={P}% cost={C}",
                j.Id, prev, j.State,
                j.Progress?.ToString(CultureInfo.InvariantCulture) ?? "?",
                j.CostUsd?.ToString("0.####", CultureInfo.InvariantCulture) ?? "?");
        else
            logger.LogInformation("  {Id}: {State} progress={P}%",
                j.Id, j.State, j.Progress?.ToString(CultureInfo.InvariantCulture) ?? "?");
        prevState[j.Id] = j.State;
    }

    var ready = ours.Where(j => j.State == "done" && !downloaded.Contains(j.Id)).ToList();
    foreach (var (j, idx) in ready.Select((j, i) => (j, i)))
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("Downloading [{N}/{T}] {Id} (~{Size} bytes)",
            idx + 1, ready.Count, j.Id, j.PackageSize?.ToString("N0") ?? "?");
        var files = await api.ListFilesAsync(j.Id);
        var dir = Path.Combine(outputRoot, j.Id);
        Directory.CreateDirectory(dir);

        var bytes = await DownloadFilesAsync(api, files, dir, maxDownloadConcurrency, verifySha256, logger);
        downloaded.Add(j.Id);

        var mb = bytes / 1_048_576.0;
        var mbps = mb / Math.Max(sw.Elapsed.TotalSeconds, 0.001);
        logger.LogInformation("Job {Id} done: {Files} file(s), {MB:F2} MB in {Elapsed:F1}s ({Speed:F2} MB/s)",
            j.Id, files.Count, mb, sw.Elapsed.TotalSeconds, mbps);
    }

    if (downloaded.Count >= submittedIds.Count)
        break;

    logger.LogInformation("Sleeping {Seconds}s before next poll...", pollSeconds);
    await Task.Delay(TimeSpan.FromSeconds(pollSeconds));
}

logger.LogInformation("All {Count} job(s) downloaded in {Elapsed:F1}s total.", downloaded.Count, pollStart.Elapsed.TotalSeconds);
return 0;

static async Task<long> DownloadFilesAsync(DatabentoBatchApi api, IReadOnlyList<BatchFile> files, string dir, int concurrency, bool verify, ILogger logger)
{
    using var sem = new SemaphoreSlim(concurrency);
    long total = 0;
    var gate = new object();
    var tasks = files.Select(async f =>
    {
        await sem.WaitAsync();
        try
        {
            var path = Path.Combine(dir, f.Filename);
            if (File.Exists(path) && new FileInfo(path).Length == f.Size)
            {
                if (!verify || await Sha256MatchesAsync(path, f.Hash))
                {
                    logger.LogInformation("  SKIP {File} (already on disk, {Bytes:N0} bytes)", f.Filename, f.Size ?? 0);
                    lock (gate) total += f.Size ?? 0;
                    return;
                }
                logger.LogWarning("  HASH MISMATCH on existing {File}; re-downloading", f.Filename);
                File.Delete(path);
            }

            var url = f.Urls?.Https ?? throw new InvalidOperationException($"No HTTPS URL for {f.Filename}");
            var written = await api.DownloadFileAsync(url, path, f.Size);
            lock (gate) total += written;

            if (verify && !await Sha256MatchesAsync(path, f.Hash))
                throw new InvalidOperationException($"SHA256 mismatch: {f.Filename}");
            if (verify)
                logger.LogInformation("  SHA256 OK {File}", f.Filename);
        }
        finally { sem.Release(); }
    });
    await Task.WhenAll(tasks);
    return total;
}

static async Task<bool> Sha256MatchesAsync(string path, string? expected)
{
    if (string.IsNullOrWhiteSpace(expected)) return true;
    var normalized = expected.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? expected["sha256:".Length..] : expected;
    await using var stream = File.OpenRead(path);
    var hash = await SHA256.HashDataAsync(stream);
    return Convert.ToHexString(hash).ToLowerInvariant().Equals(normalized, StringComparison.OrdinalIgnoreCase);
}
