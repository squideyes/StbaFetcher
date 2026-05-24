using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using StbaFetcher.OutputFormatters;
using Microsoft.Extensions.Logging;
using SquidEyes.Pricing;

namespace StbaFetcher;

/// <summary>
/// Top-level pipeline: enumerate trade dates, submit one batch job per date for the symbols
/// still missing output, poll until done, download each <c>.dbn.zst</c>, convert to
/// <c>STBA + STBA.CSV</c> for both <c>MTH</c> and <c>DTH</c> sessions, then delete the source.
/// </summary>
internal sealed class TickDataDownloader
{
    private const string Dataset = "GLBX.MDP3";
    private const string Schema = "mbp-1";
    private const string STypeIn = "continuous";
    private const string STypeOut = "instrument_id";
    private const int PollSeconds = 15;
    private const int SubmitDelaySeconds = 4;

    private readonly DatabentoBatchApi _api;
    private readonly ILogger _logger;
    private readonly Settings _settings;

    public TickDataDownloader(DatabentoBatchApi api, Settings settings, ILogger logger)
    {
        _api = api;
        _settings = settings;
        _logger = logger;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var allDates = DateOnlyExtenders.EnumerateTradeDates(_settings.From, _settings.Until).ToList();
        if (allDates.Count == 0)
        {
            _logger.LogWarning("No trade dates in [{From:yyyy-MM-dd}..{Until:yyyy-MM-dd}].",
                _settings.From, _settings.Until);
            return ExitCode.Success;
        }

        _logger.LogInformation(
            "Symbols={Symbols}  Range={From:yyyy-MM-dd}..{Until:yyyy-MM-dd} ({Days} trade date(s))  SaveTo={SaveTo}",
            string.Join(",", _settings.Symbols),
            _settings.From, _settings.Until, allDates.Count, _settings.SaveTo);

        var work = PlanWork(allDates);
        if (work.Count == 0)
        {
            _logger.LogInformation("All outputs already exist; nothing to do (use --overwrite to force).");
            return ExitCode.Success;
        }

        _logger.LogInformation("Submitting {Jobs} batch job(s) (one per trade date that has missing symbols).",
            work.Count);

        var submissions = await SubmitJobsAsync(work, cancellationToken).ConfigureAwait(false);
        await PollAndProcessAsync(submissions, cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private record JobSubmission(string JobId, DateOnly Date, IReadOnlyList<Symbol> Symbols);

    private List<(DateOnly Date, IReadOnlyList<Symbol> Symbols)> PlanWork(IReadOnlyList<DateOnly> allDates)
    {
        var plan = new List<(DateOnly, IReadOnlyList<Symbol>)>();
        foreach (var date in allDates)
        {
            var missing = _settings.Overwrite
                ? _settings.Symbols
                : _settings.Symbols
                    .Where(s => !OutputPaths.AllOutputsExist(_settings.SaveTo, s, date))
                    .ToList();
            if (missing.Count > 0)
                plan.Add((date, missing));
            else
                _logger.LogDebug("Skipping {Date:yyyy-MM-dd} — all outputs already exist.", date);
        }
        return plan;
    }

    private async Task<List<JobSubmission>> SubmitJobsAsync(
        IReadOnlyList<(DateOnly Date, IReadOnlyList<Symbol> Symbols)> work,
        CancellationToken cancellationToken)
    {
        // Submit covers the full DTH window (08:00..16:00 ET) so a single download
        // feeds both the MTH and DTH accumulators.
        var (startEt, endEt) = SessionKind.DTH.ToTimes();

        var submissions = new List<JobSubmission>(work.Count);
        decimal estCost = 0m;

        for (var i = 0; i < work.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (date, symbols) = work[i];
            var (startUtc, endUtc) = EasternTime.WindowToUtc(date, startEt, endEt);
            var request = new BatchSubmitRequest(
                Dataset: Dataset,
                Symbols: symbols.Select(s => s + ".c.0").ToArray(),
                Schema: Schema,
                Start: startUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                End: endUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                Encoding: "dbn",
                Compression: "zstd",
                SplitDuration: "day",
                SplitSymbols: true,
                STypeIn: STypeIn,
                STypeOut: STypeOut,
                LocalDate: date);

            _logger.LogInformation("[{N}/{Total}] SUBMIT {Date:yyyy-MM-dd} symbols=[{Symbols}]",
                i + 1, work.Count, date, string.Join(",", symbols));

            var job = await _api.SubmitJobAsync(request).ConfigureAwait(false);
            submissions.Add(new JobSubmission(job.Id, date, symbols));
            if (job.CostUsd is { } cost) estCost += cost;

            if (i < work.Count - 1)
                await Task.Delay(TimeSpan.FromSeconds(SubmitDelaySeconds), cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Submission complete: {Count} job(s), est_cost=${Cost:0.####}",
            submissions.Count, estCost);
        return submissions;
    }

    private async Task PollAndProcessAsync(
        IReadOnlyList<JobSubmission> submissions, CancellationToken cancellationToken)
    {
        var byId = submissions.ToDictionary(s => s.JobId, StringComparer.OrdinalIgnoreCase);
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var prevState = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sinceFilter = submissions.Min(s => s.Date).AddDays(-7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var totalTimer = Stopwatch.StartNew();
        var cycle = 0;

        while (done.Count < submissions.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            cycle++;
            _logger.LogInformation("=== Poll #{Cycle}  elapsed={Elapsed:F0}s  done={Done}/{Total} ===",
                cycle, totalTimer.Elapsed.TotalSeconds, done.Count, submissions.Count);

            var allJobs = await _api.ListJobsAsync("queued,processing,done", since: sinceFilter)
                .ConfigureAwait(false);
            var ours = allJobs.Where(j => byId.ContainsKey(j.Id)).ToList();

            foreach (var j in ours)
            {
                var prev = prevState.GetValueOrDefault(j.Id, "(new)");
                if (!string.Equals(prev, j.State, StringComparison.OrdinalIgnoreCase))
                    _logger.LogInformation("  {Id}: {Prev} -> {New} progress={P}%",
                        j.Id, prev, j.State, j.Progress?.ToString(CultureInfo.InvariantCulture) ?? "?");
                prevState[j.Id] = j.State;
            }

            foreach (var j in ours.Where(j => j.State == "done" && !done.Contains(j.Id)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ProcessJobAsync(byId[j.Id], cancellationToken).ConfigureAwait(false);
                done.Add(j.Id);
            }

            if (done.Count >= submissions.Count)
                break;

            _logger.LogInformation("Sleeping {Seconds}s before next poll...", PollSeconds);
            await Task.Delay(TimeSpan.FromSeconds(PollSeconds), cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("All {Count} job(s) processed in {Elapsed:F1}s.",
            done.Count, totalTimer.Elapsed.TotalSeconds);
    }

    private async Task ProcessJobAsync(JobSubmission submission, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var allFiles = await _api.ListFilesAsync(submission.JobId).ConfigureAwait(false);
        var dbnFiles = allFiles.Where(IsDbnFile).ToList();
        var workDir = Path.Combine(_settings.SaveTo, ".staging", submission.JobId);
        Directory.CreateDirectory(workDir);

        _logger.LogInformation("Job {JobId} ({Date:yyyy-MM-dd}) ready: {Files} DBN file(s).",
            submission.JobId, submission.Date, dbnFiles.Count);

        await DownloadFilesAsync(dbnFiles, workDir, cancellationToken).ConfigureAwait(false);

        foreach (var f in dbnFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stagedPath = Path.Combine(workDir, f.Filename);
            await ConvertSingleFileAsync(stagedPath, submission.Date, cancellationToken)
                .ConfigureAwait(false);
        }

        TryCleanupWorkDir(workDir);
        _logger.LogInformation("Job {JobId} complete in {Elapsed:F1}s.",
            submission.JobId, sw.Elapsed.TotalSeconds);
    }

    private async Task ConvertSingleFileAsync(string dbnPath, DateOnly date, CancellationToken cancellationToken)
    {
        var resolved = await ResolveSymbolContractAsync(dbnPath, date).ConfigureAwait(false);
        if (resolved is null)
        {
            _logger.LogWarning("Could not resolve symbol/contract for {File}; leaving in staging.",
                Path.GetFileName(dbnPath));
            return;
        }

        var (symbol, contract) = resolved.Value;
        Directory.CreateDirectory(OutputPaths.Directory(_settings.SaveTo, symbol, date));

        var emitters = new List<IMbp1Emitter>
        {
            new StbaEmitter(OutputPaths.StbaPath(_settings.SaveTo, symbol, date, contract, SessionKind.MTH),
                symbol, contract, date, SessionKind.MTH),
            new StbaCsvEmitter(OutputPaths.StbaCsvPath(_settings.SaveTo, symbol, date, contract, SessionKind.MTH),
                symbol, contract, date, SessionKind.MTH),
            new StbaEmitter(OutputPaths.StbaPath(_settings.SaveTo, symbol, date, contract, SessionKind.DTH),
                symbol, contract, date, SessionKind.DTH),
            new StbaCsvEmitter(OutputPaths.StbaCsvPath(_settings.SaveTo, symbol, date, contract, SessionKind.DTH),
                symbol, contract, date, SessionKind.DTH),
        };

        try
        {
            var converter = new DbnMbp1Converter(AppLogging.CreateLogger<DbnMbp1Converter>());
            await converter.ConvertAsync(dbnPath, emitters).ConfigureAwait(false);
        }
        finally
        {
            foreach (var em in emitters) await em.DisposeAsync().ConfigureAwait(false);
        }

        TryDelete(dbnPath);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task<(Symbol Symbol, Contract Contract)?> ResolveSymbolContractAsync(string dbnPath, DateOnly date)
    {
        try
        {
            var meta = DbnMetadataReader.ReadFromFile(dbnPath);
            foreach (var req in meta.Symbols)
            {
                var resolved = meta.Resolve(req, date);
                if (string.IsNullOrEmpty(resolved)) continue;

                if (int.TryParse(resolved, out _))
                {
                    var raw = await _api.ResolveSymbolAsync(Dataset, resolved, "instrument_id", "raw_symbol",
                        date, date.AddDays(1)).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(raw))
                    {
                        _logger.LogInformation("Resolved {Req} -> instrument_id={InstId} -> {Raw}",
                            req, resolved, raw);
                        resolved = raw;
                    }
                }

                var parsed = SymbolContractParser.TryParse(resolved, date);
                if (parsed is not null) return parsed;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("DBN metadata parse failed for {File}: {Msg}",
                Path.GetFileName(dbnPath), ex.Message);
        }
        return null;
    }

    private async Task DownloadFilesAsync(
        IReadOnlyList<BatchFile> files, string workDir, CancellationToken cancellationToken)
    {
        using var sem = new SemaphoreSlim(_settings.Threads);
        var tasks = files.Select(async f =>
        {
            await sem.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var path = Path.Combine(workDir, f.Filename);
                if (File.Exists(path) && new FileInfo(path).Length == f.Size
                    && await Sha256MatchesAsync(path, f.Hash).ConfigureAwait(false))
                {
                    _logger.LogInformation("  SKIP {File} (already staged)", f.Filename);
                    return;
                }
                if (File.Exists(path)) File.Delete(path);

                var url = f.Urls?.Https ?? throw new InvalidOperationException($"No HTTPS URL for {f.Filename}");
                await _api.DownloadFileAsync(url, path, f.Size).ConfigureAwait(false);

                if (!await Sha256MatchesAsync(path, f.Hash).ConfigureAwait(false))
                    throw new InvalidOperationException($"SHA256 mismatch: {f.Filename}");
            }
            finally { sem.Release(); }
        });
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static async Task<bool> Sha256MatchesAsync(string path, string? expected)
    {
        if (string.IsNullOrWhiteSpace(expected)) return true;
        var normalized = expected.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? expected["sha256:".Length..]
            : expected;
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant()
            .Equals(normalized, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDbnFile(BatchFile f) =>
        f.Filename.EndsWith(".dbn.zst", StringComparison.OrdinalIgnoreCase) ||
        f.Filename.EndsWith(".dbn", StringComparison.OrdinalIgnoreCase);

    private void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) { _logger.LogWarning("Could not delete {File}: {Msg}", path, ex.Message); }
    }

    private void TryCleanupWorkDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch (Exception ex) { _logger.LogDebug("Could not remove staging dir {Dir}: {Msg}", dir, ex.Message); }
    }
}
