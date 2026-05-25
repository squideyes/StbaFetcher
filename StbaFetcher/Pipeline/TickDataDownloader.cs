using System.Diagnostics;
using System.Globalization;
using StbaFetcher.OutputFormatters;
using Microsoft.Extensions.Logging;
using SquidEyes.Pricing;

namespace StbaFetcher;

/// <summary>
/// Top-level pipeline: enumerate trade dates × requested symbols, then for each
/// <c>(symbol, date)</c> still missing on disk, stream <c>timeseries.get_range</c>
/// straight to a staging file, parse the DBN, convert to <c>STBA</c> for both the
/// <c>MTH</c> and <c>DTH</c> sessions, and delete the source. Up to
/// <see cref="Settings.Threads"/> requests run in parallel; the first files land on
/// disk within seconds of pressing Enter.
/// </summary>
internal sealed class TickDataDownloader
{
    private const string Dataset = "GLBX.MDP3";
    private const string Schema = "mbp-1";
    private const string STypeIn = "continuous";
    private const string STypeOut = "instrument_id";

    private readonly DatabentoApi _api;
    private readonly ILogger _logger;
    private readonly Settings _settings;

    public TickDataDownloader(DatabentoApi api, Settings settings, ILogger logger)
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

        var pending = PlanPending(allDates);
        if (pending.Count == 0)
        {
            _logger.LogInformation("All outputs already exist; nothing to do (use --overwrite to force).");
            return ExitCode.Success;
        }

        if (_settings.MaxDates is { } cap)
        {
            var distinctDates = pending.Select(p => p.Date).Distinct().ToList();
            if (distinctDates.Count > cap)
            {
                var keep = new HashSet<DateOnly>(distinctDates.Take(cap));
                var remaining = distinctDates.Count - cap;
                _logger.LogInformation(
                    "Capping this run at {Cap} of {Total} pending date(s) (--max-dates); {Remaining} will wait for the next run.",
                    cap, distinctDates.Count, remaining);
                pending = pending.Where(p => keep.Contains(p.Date)).ToList();
            }
        }

        _logger.LogInformation(
            "Streaming {N} (symbol, date) request(s) at parallelism={Threads}.",
            pending.Count, _settings.Threads);

        // Staging lives in the OS temp dir, not inside SaveTo — keeps the user's
        // output folder clean and lets the OS reclaim leaked files if we crash.
        var stagingDir = Path.Combine(Path.GetTempPath(), "StbaFetcher");
        Directory.CreateDirectory(stagingDir);

        var totalTimer = Stopwatch.StartNew();
        var completed = 0;
        using var sem = new SemaphoreSlim(_settings.Threads);
        var total = pending.Count;

        var tasks = pending.Select(async pair =>
        {
            await sem.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var index = Interlocked.Increment(ref completed);
                await FetchAndConvertAsync(pair.Symbol, pair.Date, stagingDir, index, total, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                sem.Release();
            }
        });
        await Task.WhenAll(tasks).ConfigureAwait(false);

        TryCleanupStaging(stagingDir);
        _logger.LogInformation("All {Count} (symbol, date) request(s) processed in {Elapsed:F1}s.",
            total, totalTimer.Elapsed.TotalSeconds);
        return ExitCode.Success;
    }

    private List<(DateOnly Date, Symbol Symbol)> PlanPending(IReadOnlyList<DateOnly> allDates)
    {
        var plan = new List<(DateOnly, Symbol)>();
        foreach (var date in allDates)
        foreach (var symbol in _settings.Symbols)
        {
            if (_settings.Overwrite || !OutputPaths.AllOutputsExist(_settings.SaveTo, symbol, date))
                plan.Add((date, symbol));
        }
        return plan;
    }

    private async Task FetchAndConvertAsync(
        Symbol symbol, DateOnly date, string stagingDir, int index, int total,
        CancellationToken cancellationToken)
    {
        var (startEt, endEt) = SessionKind.DTH.ToTimes();
        var (startUtc, endUtc) = EasternTime.WindowToUtc(date, startEt, endEt);
        var dbnPath = Path.Combine(stagingDir, $"{symbol}_{date:yyyyMMdd}.dbn.zst");
        var sw = Stopwatch.StartNew();

        _logger.LogInformation("[{N}/{Total}] FETCH {Date:yyyy-MM-dd} {Symbol}", index, total, date, symbol);

        // Continuous front month by VOLUME (.v.0) — the contract traders actually
        // worked yesterday. Volume crosses to the new contract 1-3 days before OI
        // does, so .v.0 returns the new front on settlement day (when .n.0 would
        // still report the dying contract because yesterday's EOD OI was still in
        // it). .c.0 (calendar-nearest) is unusable for GC because gold lists every
        // month but only G/J/M/Q/V/Z carry real liquidity.
        await _api.StreamGetRangeAsync(
            dataset: Dataset, symbol: symbol + ".v.0", schema: Schema,
            startUtc: startUtc, endUtc: endUtc,
            stypeIn: STypeIn, stypeOut: STypeOut,
            destPath: dbnPath, cancellationToken: cancellationToken).ConfigureAwait(false);

        var converted = await ConvertSingleFileAsync(dbnPath, date, cancellationToken).ConfigureAwait(false);
        if (converted)
        {
            TryDelete(dbnPath);
            _logger.LogInformation("[{N}/{Total}] DONE  {Date:yyyy-MM-dd} {Symbol} in {Elapsed:F1}s.",
                index, total, date, symbol, sw.Elapsed.TotalSeconds);
        }
        else
        {
            _logger.LogWarning(
                "[{N}/{Total}] UNRESOLVED {Date:yyyy-MM-dd} {Symbol} — DBN kept at {Path} ({Bytes:N0} bytes).",
                index, total, date, symbol, dbnPath, new FileInfo(dbnPath).Length);
        }
    }

    private async Task<bool> ConvertSingleFileAsync(string dbnPath, DateOnly date, CancellationToken cancellationToken)
    {
        var resolved = await ResolveSymbolContractAsync(dbnPath, date, cancellationToken).ConfigureAwait(false);
        if (resolved is null)
            return false;

        var (symbol, contract) = resolved.Value;
        Directory.CreateDirectory(OutputPaths.Directory(_settings.SaveTo, symbol, date));

        var emitters = new List<IMbp1Emitter>
        {
            new StbaEmitter(OutputPaths.StbaPath(_settings.SaveTo, symbol, date, contract, SessionKind.MTH),
                symbol, contract, date, SessionKind.MTH),
            new StbaEmitter(OutputPaths.StbaPath(_settings.SaveTo, symbol, date, contract, SessionKind.DTH),
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

        cancellationToken.ThrowIfCancellationRequested();
        return true;
    }

    private async Task<(Symbol Symbol, Contract Contract)?> ResolveSymbolContractAsync(
        string dbnPath, DateOnly date, CancellationToken cancellationToken)
    {
        try
        {
            var meta = DbnMetadataReader.ReadFromFile(dbnPath);
            foreach (var req in meta.Symbols)
            {
                var resolved = meta.Resolve(req, date);
                if (string.IsNullOrEmpty(resolved)) continue;

                if (int.TryParse(resolved, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                {
                    var raw = await _api.ResolveSymbolAsync(Dataset, resolved, "instrument_id", "raw_symbol",
                        date, date.AddDays(1), cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(raw))
                        resolved = raw;
                }

                var parsed = SymbolContractParser.TryParse(resolved, date);
                if (parsed is { } p)
                {
                    _logger.LogInformation("Resolved {Req} -> {Sym}{Code}",
                        req, p.Symbol, p.Contract.Code);
                    return p;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("DBN metadata parse failed for {File}: {Msg}",
                Path.GetFileName(dbnPath), ex.Message);
        }
        return null;
    }

    private void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { _logger.LogWarning("Could not delete {File}: {Msg}", path, ex.Message); }
    }

    private void TryCleanupStaging(string dir)
    {
        try
        {
            if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                Directory.Delete(dir);
        }
        catch (Exception ex) { _logger.LogDebug("Could not remove staging dir {Dir}: {Msg}", dir, ex.Message); }
    }
}
