using System.Globalization;
using SquidEyes.Pricing;

namespace DatabentoDbnDownloader;

/// <summary>Parsed command-line arguments for the DBN downloader CLI.</summary>
internal sealed class Settings
{
    public const string DefaultSaveTo = @"%MYDOCS%\DataBento";
    public const int DefaultThreads = 4;

    public IReadOnlyList<Symbol> Symbols { get; private init; } = [];
    public DateOnly From { get; private init; }
    public DateOnly Until { get; private init; }
    public string SaveTo { get; private init; } = PathTokens.Expand(DefaultSaveTo);
    public int Threads { get; private init; } = DefaultThreads;
    public bool Overwrite { get; private init; }
    public bool Verbose { get; private init; }
    public bool ShowHelp { get; private init; }
    public string? SetApiKey { get; private init; }

    public static string HelpText =>
        $$"""
        DatabentoDbnDownloader — fetch CME futures MBP-1 from Databento and emit STBA + STBA.CSV.

        For each (symbol, trade-date) the tool produces four files:
          {Symbol}_{yyyyMMdd}_{Contract}_DB_MTH_ET.stba       (08:00..12:00 ET)
          {Symbol}_{yyyyMMdd}_{Contract}_DB_MTH_ET.stba.csv
          {Symbol}_{yyyyMMdd}_{Contract}_DB_DTH_ET.stba       (08:00..16:00 ET)
          {Symbol}_{yyyyMMdd}_{Contract}_DB_DTH_ET.stba.csv

        Output is laid out as {SaveTo}/{Symbol}/{Year}/<filename>.

        Usage:
          DatabentoDbnDownloader --symbols <list> [--from <date>] [--until <date>] [options]
          DatabentoDbnDownloader --set-key <db-...>

        Required:
          --symbols <list>   Comma-separated root symbols (ES, NQ, CL, GC, TY, FV, US, JY, EU, BP).
                             Continuous front month (.c.0) is implied.

        Options:
          --from <date>      Inclusive ET trade-date, yyyy-MM-dd. Must be a valid trade date.
                             Default: the earliest supported trade date.
          --until <date>     Inclusive ET trade-date, yyyy-MM-dd. Must be a valid trade date.
                             Default: yesterday's trade date (latest trade date strictly before today ET).
          --saveto <folder>  Output folder. Default: {{DefaultSaveTo}}
                             Supports tokens: %MYDOCS%, %DESKTOP%, %USERPROFILE%, %LOCALAPPDATA%,
                             plus any defined environment variable.
          --threads <n>      Concurrent file downloads per batch job (default: {{DefaultThreads}}).
          --overwrite        Re-download (symbol, date) tuples whose output files already exist.
          --verbose          Enable debug-level logging.
          --set-key <key>    Save your Databento API key (DPAPI-encrypted, per Windows user) and exit.
          --help, -h         Show this help.

        The API key is read from %LOCALAPPDATA%\DatabentoDbnDownloader\api-key.dat. Set it once with:
          DatabentoDbnDownloader --set-key db-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxx

        Example:
          DatabentoDbnDownloader --symbols ES,NQ --from 2026-05-04 --until 2026-05-08
        """;

    /// <exception cref="ArgumentException">An argument is missing, unknown, or malformed.</exception>
    public static Settings Parse(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
            return new Settings { ShowHelp = true };

        string? symbols = null;
        string? from = null;
        string? until = null;
        string? saveTo = null;
        string? threads = null;
        string? setKey = null;
        var overwrite = false;
        var verbose = false;

        for (var i = 0; i < args.Length; i++)
        {
            var key = args[i];
            switch (key)
            {
                case "--symbols": symbols = NextValue(args, ref i, key); break;
                case "--from": from = NextValue(args, ref i, key); break;
                case "--until": until = NextValue(args, ref i, key); break;
                case "--saveto": saveTo = NextValue(args, ref i, key); break;
                case "--threads": threads = NextValue(args, ref i, key); break;
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
        var today = EasternTime.TodayEt();

        var parsedFrom = string.IsNullOrWhiteSpace(from)
            ? DateOnlyExtenders.EarliestTradeDate()
            : ParseTradeDate(from, "--from");
        var parsedUntil = string.IsNullOrWhiteSpace(until)
            ? today.LatestTradeDateBefore()
            : ParseTradeDate(until, "--until");
        if (parsedUntil < parsedFrom)
            throw new ArgumentException($"--until ({parsedUntil:yyyy-MM-dd}) must be on or after --from ({parsedFrom:yyyy-MM-dd}).");

        return new Settings
        {
            Symbols = parsedSymbols,
            From = parsedFrom,
            Until = parsedUntil,
            SaveTo = PathTokens.Expand(string.IsNullOrWhiteSpace(saveTo) ? DefaultSaveTo : saveTo),
            Threads = threads is null ? DefaultThreads : ParseThreads(threads),
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
            if (!Enum.TryParse<Symbol>(p, ignoreCase: true, out var sym))
                throw new ArgumentException(
                    $"Unknown symbol '{p}'. Supported: {string.Join(", ", Enum.GetNames<Symbol>())}.");
            if (!list.Contains(sym))
                list.Add(sym);
        }
        return list;
    }

    private static string NextValue(string[] args, ref int i, string key)
    {
        if (i + 1 >= args.Length)
            throw new ArgumentException($"Argument '{key}' expects a value.");
        return args[++i];
    }

    private static DateOnly ParseTradeDate(string value, string argName)
    {
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
        {
            throw new ArgumentException($"Could not parse {argName} value '{value}'. Use yyyy-MM-dd.");
        }
        if (!parsed.IsTradeDate())
        {
            throw new ArgumentException(
                $"{argName} ({parsed:yyyy-MM-dd}) is not a valid trade date " +
                "(weekend, holiday, or out of the supported calendar).");
        }
        return parsed;
    }

    private static int ParseThreads(string value)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0)
            return n;
        throw new ArgumentException($"--threads must be a positive integer, got '{value}'.");
    }
}
