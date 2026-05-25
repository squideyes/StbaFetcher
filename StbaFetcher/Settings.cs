using System.Globalization;
using SquidEyes.Pricing;

namespace StbaFetcher;

/// <summary>Parsed command-line arguments for the DBN downloader CLI.</summary>
internal sealed class Settings
{
    public const string DefaultSaveTo = @"%MYDOCS%\DataBento\STBA";
    public const int DefaultThreads = 4;

    public IReadOnlyList<Symbol> Symbols { get; private init; } = [];
    public DateOnly From { get; private init; }
    public DateOnly Until { get; private init; }
    public string SaveTo { get; private init; } = PathTokens.Expand(DefaultSaveTo);
    public int Threads { get; private init; } = DefaultThreads;
    public int? MaxDates { get; private init; }
    public bool Overwrite { get; private init; }
    public bool Verbose { get; private init; }
    public bool ShowHelp { get; private init; }
    public string? SetApiKey { get; private init; }

    public static string HelpText =>
        $$"""
        StbaFetcher — fetch CME futures MBP-1 from Databento and emit STBA.

        For each (symbol, trade-date) the tool produces two files:
          {Symbol}_{yyyyMMdd}_{Contract}_DB_MTH_ET.stba       (08:00..12:00 ET)
          {Symbol}_{yyyyMMdd}_{Contract}_DB_DTH_ET.stba       (08:00..16:00 ET)

        Output is laid out as {SaveTo}/{Symbol}/{Year}/<filename>.

        Usage:
          StbaFetcher --symbols <list> [options]
          StbaFetcher --set-key <db-...>

        Required:
          --symbols <list>   Comma-separated root symbols (ES, NQ, CL, GC, TY, FV, US, JY, EU, BP),
                             or 'ALL' to expand to every supported symbol. Mixed lists like
                             'ALL,NQ' are deduped. Continuous front month (.c.0) is implied.

        Options:
          --alldates         Fetch from the earliest supported trade date instead of the default
                             360-day window (Databento bills per GB — use deliberately).
          --saveto <folder>  Output folder. Default: {{DefaultSaveTo}}
                             Supports tokens: %MYDOCS%, %DESKTOP%, %USERPROFILE%, %LOCALAPPDATA%,
                             plus any defined environment variable.
          --threads <n>      Concurrent (symbol, date) streaming requests (default: {{DefaultThreads}}).
          --max-dates <n>    Process at most N trade dates this run (oldest missing first), then
                             exit. Lets you take small bites instead of waiting through the full
                             submit-all-then-poll-all cycle; re-run to pick up where you left off.
          --overwrite        Re-download (symbol, date) tuples whose output files already exist.
          --verbose          Enable debug-level logging.
          --set-key <key>    Save your Databento API key (DPAPI-encrypted, per Windows user) and exit.
          --help, -h         Show this help.

        The fetch always runs up to yesterday's trade date (ET). By default the start is the
        first trade date on or after (yesterday − 360 days), keeping the window safely below
        any rolling-12-month quota; pass --alldates to start at the earliest supported trade
        date instead.

        The API key is stored in Windows Credential Manager (Generic credential
        '{{SecretStore.TargetName}}', DPAPI-protected, per Windows user). Set it once with:
          StbaFetcher --set-key db-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxx

        Examples:
          StbaFetcher --symbols ALL
          StbaFetcher --symbols ES,NQ --alldates
        """;

    /// <exception cref="ArgumentException">An argument is missing, unknown, or malformed.</exception>
    public static Settings Parse(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
            return new Settings { ShowHelp = true };

        string? symbols = null;
        string? saveTo = null;
        string? threads = null;
        string? maxDates = null;
        string? setKey = null;
        var all = false;
        var overwrite = false;
        var verbose = false;

        for (var i = 0; i < args.Length; i++)
        {
            var key = args[i];
            switch (key)
            {
                case "--symbols": symbols = NextValue(args, ref i, key); break;
                case "--saveto": saveTo = NextValue(args, ref i, key); break;
                case "--threads": threads = NextValue(args, ref i, key); break;
                case "--max-dates": maxDates = NextValue(args, ref i, key); break;
                case "--alldates": all = true; break;
                case "--overwrite": overwrite = true; break;
                case "--verbose": verbose = true; break;
                case "--set-key": setKey = NextValue(args, ref i, key); break;
                default:
                    throw new ArgumentException($"Unknown argument '{key}'. Use --help for usage.");
            }
        }

        if (setKey is not null)
        {
            if (string.IsNullOrWhiteSpace(setKey))
                throw new ArgumentException("--set-key requires a non-empty value.");
            return new Settings { SetApiKey = setKey };
        }

        if (string.IsNullOrWhiteSpace(symbols))
            throw new ArgumentException("--symbols is required.");

        var parsedSymbols = ParseSymbols(symbols);
        var until = EasternTime.TodayEt().LatestTradeDateBefore();
        var earliest = DateOnlyExtenders.EarliestTradeDate();

        DateOnly from;
        if (all)
        {
            from = earliest;
        }
        else
        {
            // Anchor a 360-day window on yesterday's trade date (5 days under a calendar
            // year, to stay safely below any rolling-12-month quota even in leap years)
            // and snap forward to a valid trade date; clamp to the earliest supported
            // date when the anchor predates the calendar.
            var anchor = until.AddDays(-360);
            from = anchor < earliest ? earliest : anchor;
            while (from <= until && !from.IsTradeDate())
                from = from.AddDays(1);
        }

        return new Settings
        {
            Symbols = parsedSymbols,
            From = from,
            Until = until,
            SaveTo = PathTokens.Expand(string.IsNullOrWhiteSpace(saveTo) ? DefaultSaveTo : saveTo),
            Threads = threads is null ? DefaultThreads : ParseThreads(threads),
            MaxDates = maxDates is null ? null : ParsePositiveInt(maxDates, "--max-dates"),
            Overwrite = overwrite,
            Verbose = verbose,
        };
    }

    private static IReadOnlyList<Symbol> ParseSymbols(string raw)
    {
        var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            throw new ArgumentException("--symbols must list at least one root symbol.");

        var list = new List<Symbol>(parts.Length);
        foreach (var p in parts)
        {
            if (string.Equals(p, "ALL", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var sym in Enum.GetValues<Symbol>())
                    if (!list.Contains(sym))
                        list.Add(sym);
                continue;
            }
            if (!Enum.TryParse<Symbol>(p, ignoreCase: true, out var parsed))
                throw new ArgumentException(
                    $"Unknown symbol '{p}'. Supported: {string.Join(", ", Enum.GetNames<Symbol>())}, ALL.");
            if (!list.Contains(parsed))
                list.Add(parsed);
        }
        return list;
    }

    private static string NextValue(string[] args, ref int i, string key)
    {
        if (i + 1 >= args.Length)
            throw new ArgumentException($"Argument '{key}' expects a value.");
        return args[++i];
    }

    private static int ParseThreads(string value) => ParsePositiveInt(value, "--threads");

    private static int ParsePositiveInt(string value, string argName)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0)
            return n;
        throw new ArgumentException($"{argName} must be a positive integer, got '{value}'.");
    }
}
