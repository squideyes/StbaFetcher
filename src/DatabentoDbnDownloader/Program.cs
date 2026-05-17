using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using DatabentoDbnDownloader.OutputFormatters;
using DatabentoDbnDownloader.TickSets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

// === Devtest config (edit here) =====================================
var dataset = "GLBX.MDP3";
var schema = "mbp-1";
string[] requestedSymbols = ["ES.c.0"];
var stypeIn = "continuous";
var stypeOut = "instrument_id";    // continuous only outputs to instrument_id; we two-hop to raw_symbol after download
var encoding = "dbn";
var compression = "zstd";
var splitDuration = "day";
var splitSymbols = true;
var outputRoot = @"Z:\DataBento\Raw";
var startDate = new DateOnly(2025, 1, 2);
var endDateExclusive = new DateOnly(2025, 1, 3);  // single weekday
var timeRange = TimeRange.MTH;                    // 08:00–12:00 ET
var source = Source.DataBento;
var weekdaysOnly = true;
var pollSeconds = 15;
var maxDownloadConcurrency = 4;
var verifySha256 = true;
var defaultFormats = "ALL";                  // CLI: --format STBA,TBA,FULL,PARQUET,ALL  ("NONE" disables); case-insensitive
// =====================================================================

AppLogging.Init(LogLevel.Information);
var logger = AppLogging.Factory.CreateLogger("app");

// Diagnostic: `--dump <path-to-dbn>` prints parsed metadata then exits without submitting/downloading.
var dumpPath = GetArg(args, "--dump");
if (dumpPath is not null)
{
    return DumpDbnMetadata(dumpPath, logger);
}

var requestedFormats = ParseFormats(args, defaultFormats);
var (startEt, endEt) = timeRange.ToTimes();

var config = new ConfigurationBuilder()
    .AddUserSecrets(typeof(Program).Assembly, optional: true)
    .AddEnvironmentVariables()
    .Build();

var apiKey = config["DATABENTO_API_KEY"]
    ?? throw new InvalidOperationException("Set DATABENTO_API_KEY via `dotnet user-secrets set DATABENTO_API_KEY db-...` or environment variable.");

logger.LogInformation("Source:          {Source} ({Code})", source, source.ToCode());
logger.LogInformation("Dataset/Schema:  {Dataset} / {Schema}", dataset, schema);
logger.LogInformation("Symbols:         {Symbols} (stype_in={In}, stype_out={Out})", string.Join(", ", requestedSymbols), stypeIn, stypeOut);
logger.LogInformation("Time range:      {Range} ({Start}-{End} ET)", timeRange.ToCode(), startEt, endEt);
logger.LogInformation("Date range:      {Start:yyyy-MM-dd} through {End:yyyy-MM-dd}", startDate, endDateExclusive.AddDays(-1));
logger.LogInformation("Output root:     {Root}", outputRoot);
logger.LogInformation("Poll interval:   {Seconds}s   concurrency={Conc}   verify_sha256={Verify}", pollSeconds, maxDownloadConcurrency, verifySha256);
logger.LogInformation("Output formats:  {Formats}", string.Join(", ", requestedFormats.Count == 0 ? new[] { "(NONE)" } : requestedFormats.Select(f => f.ToUpperInvariant()).ToArray()));

Directory.CreateDirectory(outputRoot);

var requests = new List<(BatchSubmitRequest Req, DateOnly Date)>();
for (var d = startDate; d < endDateExclusive; d = d.AddDays(1))
{
    if (weekdaysOnly && d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        continue;
    var (startUtc, endUtc) = EasternTradingWindow.ToUtc(d, startEt, endEt);
    requests.Add((new BatchSubmitRequest(
        Dataset: dataset,
        Symbols: requestedSymbols,
        Schema: schema,
        Start: startUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
        End: endUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
        Encoding: encoding,
        Compression: compression,
        SplitDuration: splitDuration,
        SplitSymbols: splitSymbols,
        STypeIn: stypeIn,
        STypeOut: stypeOut,
        LocalDate: d), d));
}

logger.LogInformation("Built {Count} request(s)", requests.Count);
foreach (var (r, _) in requests)
    logger.LogInformation("  {Date}: {Start} -> {End} UTC", r.LocalDate, r.Start, r.End);

using var client = DatabentoHttpClient.Create(apiKey);
var api = new DatabentoBatchApi(client, AppLogging.CreateLogger<DatabentoBatchApi>());

var jobDates = new Dictionary<string, DateOnly>(StringComparer.OrdinalIgnoreCase);
var submittedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
decimal estCost = 0m;

for (var i = 0; i < requests.Count; i++)
{
    var (r, date) = requests[i];
    logger.LogInformation("[{N}/{T}] SUBMIT {Date} {Start} -> {End}", i + 1, requests.Count, r.LocalDate, r.Start, r.End);
    var job = await api.SubmitJobAsync(r);
    submittedIds.Add(job.Id);
    jobDates[job.Id] = date;
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
        var dataDate = jobDates[j.Id];
        logger.LogInformation("Downloading [{N}/{T}] {Id} (~{Size} bytes)",
            idx + 1, ready.Count, j.Id, j.PackageSize?.ToString("N0") ?? "?");
        var allFiles = await api.ListFilesAsync(j.Id);
        var files = allFiles.Where(IsDbnFile).ToList();
        var skippedJson = allFiles.Count - files.Count;
        if (skippedJson > 0)
            logger.LogInformation("Skipping {N} non-DBN file(s) (manifest/condition/metadata JSON)", skippedJson);

        var dir = Path.Combine(outputRoot, j.Id);
        Directory.CreateDirectory(dir);

        var bytes = await DownloadFilesAsync(api, files, dir, maxDownloadConcurrency, verifySha256, logger);
        downloaded.Add(j.Id);

        var mb = bytes / 1_048_576.0;
        var mbps = mb / Math.Max(sw.Elapsed.TotalSeconds, 0.001);
        logger.LogInformation("Job {Id} done: {Files} file(s), {MB:F2} MB in {Elapsed:F1}s ({Speed:F2} MB/s)",
            j.Id, files.Count, mb, sw.Elapsed.TotalSeconds, mbps);

        if (requestedFormats.Count > 0)
            await ProcessJobOutputsAsync(files, dir, dataDate, requestedSymbols, dataset, source, timeRange, requestedFormats, api, logger);
    }

    if (downloaded.Count >= submittedIds.Count)
        break;

    logger.LogInformation("Sleeping {Seconds}s before next poll...", pollSeconds);
    await Task.Delay(TimeSpan.FromSeconds(pollSeconds));
}

logger.LogInformation("All {Count} job(s) downloaded in {Elapsed:F1}s total.", downloaded.Count, pollStart.Elapsed.TotalSeconds);
return 0;

static async Task ProcessJobOutputsAsync(
    IReadOnlyList<BatchFile> files, string dir, DateOnly dataDate,
    string[] requestedSymbols, string dataset, Source source, TimeRange timeRange,
    IReadOnlyList<string> formats, DatabentoBatchApi api, ILogger logger)
{
    var converter = new DbnMbp1Converter(AppLogging.CreateLogger<DbnMbp1Converter>());

    foreach (var f in files.Where(IsDbnFile))
    {
        var originalPath = Path.Combine(dir, f.Filename);

        var resolved = await ResolveSymbolContractAsync(originalPath, requestedSymbols, dataDate, dataset, api, logger);
        if (resolved is null)
        {
            logger.LogWarning("Cannot resolve symbol/contract for {File}; skipping conversion.", f.Filename);
            continue;
        }

        var (symbol, contract) = resolved.Value;
        var baseStem = $"{symbol}_{dataDate:yyyyMMdd}_{contract.Code}_{source.ToCode()}_{timeRange.ToCode()}";
        var sourceStem = baseStem + "_UT";   // Databento DBN carries UTC timestamps
        var outputStem = baseStem + "_ET";   // all derived outputs are in Eastern Time
        var canonicalSource = Path.Combine(dir, sourceStem + ".dbn.zst");

        var inputPath = TryRename(originalPath, canonicalSource, logger);

        var emitters = BuildEmitters(formats, dir, outputStem, symbol, contract, dataDate, timeRange, logger);
        if (emitters.Count == 0) continue;

        try { await converter.ConvertAsync(inputPath, emitters); }
        finally { foreach (var em in emitters) await em.DisposeAsync(); }
    }
}

static async Task<(Symbol Symbol, Contract Contract)?> ResolveSymbolContractAsync(
    string dbnPath, string[] requestedSymbols, DateOnly dataDate,
    string dataset, DatabentoBatchApi api, ILogger logger)
{
    try
    {
        var meta = DbnMetadataReader.ReadFromFile(dbnPath);
        foreach (var req in requestedSymbols)
        {
            var resolved = meta.Resolve(req, dataDate);
            if (resolved is null) continue;

            // continuous + instrument_id gives us "5002"; translate to "ESH5" via symbology.resolve
            if (int.TryParse(resolved, out _))
            {
                var raw = await api.ResolveSymbolAsync(dataset, resolved, "instrument_id", "raw_symbol",
                    dataDate, dataDate.AddDays(1));
                if (!string.IsNullOrEmpty(raw))
                {
                    logger.LogInformation("Resolved {Req} -> instrument_id={InstId} -> {Raw}", req, resolved, raw);
                    resolved = raw;
                }
            }
            else
            {
                logger.LogInformation("Resolved {Req} -> {Resolved} via DBN metadata", req, resolved);
            }

            var parsed = SymbolContractParser.TryParse(resolved, dataDate);
            if (parsed is not null) return parsed;
        }
        logger.LogWarning("DBN metadata had no resolvable mapping for {Req} on {Date}", string.Join(",", requestedSymbols), dataDate);
    }
    catch (Exception ex)
    {
        logger.LogWarning("DBN metadata parse failed for {File}: {Msg}", Path.GetFileName(dbnPath), ex.Message);
    }

    var fallback = TryParseSymbolContractFromFilename(Path.GetFileName(dbnPath), dataDate);
    if (fallback is not null)
    {
        logger.LogInformation("Resolved from filename: {Symbol}{Contract}", fallback.Value.Symbol, fallback.Value.Contract.Code);
    }
    return fallback;
}

static (Symbol Symbol, Contract Contract)? TryParseSymbolContractFromFilename(string filename, DateOnly dataDate)
{
    // e.g. "glbx-mdp3-20250102.mbp-1.ESH5.dbn.zst" -> symbol segment "ESH5"
    var stem = filename;
    if (stem.EndsWith(".zst", StringComparison.OrdinalIgnoreCase)) stem = stem[..^4];
    if (stem.EndsWith(".dbn", StringComparison.OrdinalIgnoreCase)) stem = stem[..^4];
    var parts = stem.Split('.');
    if (parts.Length < 3) return null;
    var segment = string.Join('.', parts.Skip(2));
    return SymbolContractParser.TryParse(segment, dataDate);
}

static string TryRename(string from, string to, ILogger logger)
{
    if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) return from;
    if (!File.Exists(from)) return from;
    try
    {
        if (File.Exists(to))
        {
            logger.LogInformation("Rename skipped: {Target} already exists.", Path.GetFileName(to));
            return to;
        }
        File.Move(from, to);
        logger.LogInformation("Renamed {From} -> {To}", Path.GetFileName(from), Path.GetFileName(to));
        return to;
    }
    catch (Exception ex)
    {
        logger.LogWarning("Rename failed ({From} -> {To}): {Msg}; using original.", Path.GetFileName(from), Path.GetFileName(to), ex.Message);
        return from;
    }
}

static bool IsDbnFile(BatchFile f) =>
    f.Filename.EndsWith(".dbn.zst", StringComparison.OrdinalIgnoreCase) ||
    f.Filename.EndsWith(".dbn", StringComparison.OrdinalIgnoreCase);

static List<IMbp1Emitter> BuildEmitters(
    IReadOnlyList<string> formats, string dir, string stem,
    Symbol symbol, Contract contract, DateOnly dataDate, TimeRange range, ILogger logger)
{
    var list = new List<IMbp1Emitter>();
    foreach (var fmt in formats)
    {
        try
        {
            switch (fmt)
            {
                case "full":
                    list.Add(new FullCsvEmitter(Path.Combine(dir, stem + ".full.csv")));
                    break;
                case "parquet":
                    list.Add(new ParquetEmitter(Path.Combine(dir, stem + ".parquet")));
                    break;
                case "stba":
                    list.Add(new StbaEmitter(Path.Combine(dir, stem + ".stba"), symbol, contract, dataDate, range));
                    break;
                case "tba":
                    list.Add(new TbaCsvEmitter(Path.Combine(dir, stem + ".tba.csv"), symbol, contract, dataDate, range));
                    break;
                default:
                    logger.LogWarning("Unknown format '{Fmt}', skipping.", fmt);
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create {Fmt} emitter for {Stem}: {Msg}", fmt, stem, ex.Message);
        }
    }
    return list;
}

static string? GetArg(string[] args, string name)
{
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            return args[i + 1];
        if (args[i].StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
            return args[i][(name.Length + 1)..];
    }
    return null;
}

static int DumpDbnMetadata(string path, ILogger logger)
{
    try
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"File not found: {path}");
            return 1;
        }

        // Raw byte dump for offset diagnostics (first 256 bytes of decompressed metadata)
        DbnMetadataReader.DumpRawHeader(path, 256);

        var meta = DbnMetadataReader.ReadFromFile(path);
        Console.WriteLine($"DBN version:      v{meta.Version}");
        Console.WriteLine($"Dataset:          {meta.Dataset}");
        Console.WriteLine($"symbol_cstr_len:  {meta.SymbolCstrLen}");
        Console.WriteLine($"Requested syms:   [{string.Join(", ", meta.Symbols)}]");
        Console.WriteLine($"Mappings count:   {meta.Mappings.Count}");
        for (var i = 0; i < meta.Mappings.Count; i++)
        {
            var m = meta.Mappings[i];
            Console.WriteLine($"  mapping[{i}] native_symbol=\"{m.NativeSymbol}\" intervals={m.Intervals.Count}");
            foreach (var iv in m.Intervals)
                Console.WriteLine($"    interval: {iv.StartDate:yyyy-MM-dd}..{iv.EndDate:yyyy-MM-dd} -> \"{iv.Symbol}\"");
        }
        return 0;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"DUMP failed: {ex.Message}");
        Console.WriteLine(ex);
        return 1;
    }
}

static List<string> ParseFormats(string[] args, string defaultFormats)
{
    // Canonical lower-case names recognised internally. ALL expands to this list.
    var known = new[] { "full", "stba", "tba", "parquet" };

    string? raw = null;
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i].Equals("--format", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
        {
            raw = args[i + 1];
            break;
        }
        if (args[i].StartsWith("--format=", StringComparison.OrdinalIgnoreCase))
        {
            raw = args[i]["--format=".Length..];
            break;
        }
    }
    raw ??= defaultFormats;
    if (raw.Equals("none", StringComparison.OrdinalIgnoreCase)) return new List<string>();

    return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .SelectMany(s => s.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? known
            : new[] { s.ToLowerInvariant() })
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
}

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
