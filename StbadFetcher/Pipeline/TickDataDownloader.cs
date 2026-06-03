using System.Diagnostics;
using System.Globalization;
using StbadFetcher.Databento;
using StbadFetcher.OutputFormatters;
using Microsoft.Extensions.Logging;
using SquidEyes.Pricing;

namespace StbadFetcher;

/// <summary>
/// Top-level pipeline: enumerate trade dates × requested symbols, then for each
/// <c>(symbol, date)</c> still missing on disk, stream <c>timeseries.get_range</c>
/// straight to a staging file, parse the DBN, convert to <c>STBAD</c> (MBP-10 depth) for both the
/// <c>MTH</c> and <c>DTH</c> sessions, and delete the source. Up to
/// <see cref="Settings.Threads"/> requests run in parallel; the first files land on
/// disk within seconds of pressing Enter.
/// </summary>
internal sealed class TickDataDownloader
{
    private const string Dataset = "GLBX.MDP3";
    private const string Schema = "mbp-10";
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
        var stagingDir = Path.Combine(Path.GetTempPath(), "StbadFetcher");
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

    // A converted output: how many MBP-10 rows landed, and the two STBAD file paths
    // (Empty path strings when Ok=false, i.e. the DBN had no symbol mapping).
    private readonly record struct ConvertResult(bool Ok, long Rows, string MthPath, string DthPath);

    // Below this row count, the contract is presumed dying and we auto-probe .v.1.
    // NOTE: this threshold was calibrated on MBP-1 row counts; MBP-10 emits many more
    // records per session, so the "thin" gap likely wants re-tuning against real depth
    // data (a dying contract will still be proportionally tiny, just at a higher floor).
    private const int ThinRowThreshold = 100_000;

    // The .v.1 result must be this many times bigger than .v.0 to be preferred —
    // protects against truly thin sessions where both selectors return small files.
    private const int PreferAltMultiple = 3;

    private async Task FetchAndConvertAsync(
        Symbol symbol, DateOnly date, string stagingDir, int index, int total,
        CancellationToken cancellationToken)
    {
        var (startEt, endEt) = SessionKind.DTH.ToTimes();
        var (startUtc, endUtc) = EasternTime.WindowToUtc(date, startEt, endEt);
        var sw = Stopwatch.StartNew();

        _logger.LogInformation("[{N}/{Total}] FETCH {Date:yyyy-MM-dd} {Symbol}", index, total, date, symbol);

        // Continuous front month by VOLUME (.v.0) — the contract traders actually
        // worked yesterday. Volume crosses to the new contract 1-3 days before OI
        // does, so .v.0 returns the new front on settlement day (when .n.0 would
        // still report the dying contract because yesterday's EOD OI was still in
        // it). .c.0 (calendar-nearest) is unusable for GC because gold lists every
        // month but only G/J/M/Q/V/Z carry real liquidity.
        var primary = await FetchAndConvertOneAsync(
            symbol, date, ".v.0", stagingDir, startUtc, endUtc, cancellationToken).ConfigureAwait(false);

        if (!primary.Ok)
        {
            _logger.LogWarning("[{N}/{Total}] UNRESOLVED {Date:yyyy-MM-dd} {Symbol}",
                index, total, date, symbol);
            return;
        }

        // Auto-repair for gold day-before-FND (and similar physically-settled-roll
        // days): if .v.0 came back unusually thin, probe .v.1 (second-most-active by
        // volume). On the day the roll itself fires, yesterday's volume was still in
        // the dying contract because that's the day everyone moved out — .v.1 names
        // the contract traders moved INTO. Keep whichever has more rows.
        if (primary.Rows < ThinRowThreshold)
        {
            _logger.LogInformation("  {Date:yyyy-MM-dd} {Symbol}: .v.0 thin ({Rows:N0} rows); probing .v.1.",
                date, symbol, primary.Rows);

            var alt = await FetchAndConvertOneAsync(
                symbol, date, ".v.1", stagingDir, startUtc, endUtc, cancellationToken).ConfigureAwait(false);

            if (alt.Ok && alt.Rows > primary.Rows * PreferAltMultiple)
            {
                _logger.LogInformation(
                    "  {Date:yyyy-MM-dd} {Symbol}: kept .v.1 ({AltRows:N0} rows vs .v.0 {PrimaryRows:N0}).",
                    date, symbol, alt.Rows, primary.Rows);
                if (primary.MthPath != alt.MthPath) TryDelete(primary.MthPath);
                if (primary.DthPath != alt.DthPath) TryDelete(primary.DthPath);
            }
            else if (alt.Ok)
            {
                if (alt.MthPath != primary.MthPath) TryDelete(alt.MthPath);
                if (alt.DthPath != primary.DthPath) TryDelete(alt.DthPath);
            }
        }

        _logger.LogInformation("[{N}/{Total}] DONE  {Date:yyyy-MM-dd} {Symbol} in {Elapsed:F1}s.",
            index, total, date, symbol, sw.Elapsed.TotalSeconds);
    }

    private async Task<ConvertResult> FetchAndConvertOneAsync(
        Symbol symbol, DateOnly date, string suffix, string stagingDir,
        DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken cancellationToken)
    {
        var safeSuffix = suffix.Replace(".", "");
        var dbnPath = Path.Combine(stagingDir, $"{symbol}_{date:yyyyMMdd}_{safeSuffix}.dbn.zst");
        try
        {
            await _api.StreamGetRangeAsync(
                dataset: Dataset, symbol: symbol + suffix, schema: Schema,
                startUtc: startUtc, endUtc: endUtc,
                stypeIn: STypeIn, stypeOut: STypeOut,
                destPath: dbnPath, cancellationToken: cancellationToken).ConfigureAwait(false);

            return await ConvertSingleFileAsync(dbnPath, date, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            TryDelete(dbnPath);
        }
    }

    private async Task<ConvertResult> ConvertSingleFileAsync(string dbnPath, DateOnly date, CancellationToken cancellationToken)
    {
        var resolved = await ResolveSymbolContractAsync(dbnPath, date, cancellationToken).ConfigureAwait(false);
        if (resolved is null)
            return new ConvertResult(false, 0, string.Empty, string.Empty);

        var (symbol, contract) = resolved.Value;
        Directory.CreateDirectory(OutputPaths.Directory(_settings.SaveTo, symbol, date));

        var mthPath = OutputPaths.StbadPath(_settings.SaveTo, symbol, date, contract, SessionKind.MTH);
        var dthPath = OutputPaths.StbadPath(_settings.SaveTo, symbol, date, contract, SessionKind.DTH);

        var emitters = new List<IDepthEmitter>
        {
            new StbadEmitter(mthPath, symbol, contract, date, SessionKind.MTH),
            new StbadEmitter(dthPath, symbol, contract, date, SessionKind.DTH),
        };

        long rows;
        try
        {
            var converter = new DbnMbp10Converter(AppLogging.CreateLogger<DbnMbp10Converter>());
            rows = await converter.ConvertAsync(dbnPath, emitters).ConfigureAwait(false);
        }
        finally
        {
            foreach (var em in emitters) await em.DisposeAsync().ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new ConvertResult(true, rows, mthPath, dthPath);
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
