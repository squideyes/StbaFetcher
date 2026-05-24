using System.Globalization;
using SquidEyes.Pricing;

namespace StbaFetcher;

/// <summary>
/// Resolves the on-disk layout for converted output files. All artifacts for a
/// <c>(symbol, trade-date)</c> tuple live in <c>{SaveTo}/{Symbol}/{Year}/</c>, with canonical
/// filenames produced by <see cref="PricingFile.BuildStem(Symbol, DateOnly, Contract, Source, SessionKind)"/>.
/// </summary>
internal static class OutputPaths
{
    public static string Directory(string saveTo, Symbol symbol, DateOnly date) =>
        Path.Combine(saveTo, symbol.ToString(), date.Year.ToString(CultureInfo.InvariantCulture));

    public static string StbaPath(string saveTo, Symbol symbol, DateOnly date, Contract contract, SessionKind session) =>
        Path.Combine(Directory(saveTo, symbol, date),
            PricingFile.BuildStem(symbol, date, contract, Source.DataBento, session) + ".stba");

    public static string StbaCsvPath(string saveTo, Symbol symbol, DateOnly date, Contract contract, SessionKind session) =>
        Path.Combine(Directory(saveTo, symbol, date),
            PricingFile.BuildStem(symbol, date, contract, Source.DataBento, session) + ".stba.csv");

    /// <summary>
    /// True iff all four output files for <paramref name="symbol"/> on <paramref name="date"/>
    /// already exist (MTH + DTH × .stba + .stba.csv), under any contract suffix. Used to skip
    /// already-fetched <c>(symbol, date)</c> pairs without issuing a billed batch request.
    /// </summary>
    public static bool AllOutputsExist(string saveTo, Symbol symbol, DateOnly date)
    {
        var dir = Directory(saveTo, symbol, date);
        if (!System.IO.Directory.Exists(dir)) return false;

        var datePart = date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var sourcePart = Source.DataBento.ToCode();
        return SessionsCovered(dir, symbol, datePart, sourcePart, SessionKind.MTH)
            && SessionsCovered(dir, symbol, datePart, sourcePart, SessionKind.DTH);
    }

    private static bool SessionsCovered(string dir, Symbol symbol, string datePart, string sourcePart, SessionKind session)
    {
        var sessionPart = session.ToCode();
        var stbaGlob = $"{symbol}_{datePart}_*_{sourcePart}_{sessionPart}_ET.stba";
        var stbaCsvGlob = $"{symbol}_{datePart}_*_{sourcePart}_{sessionPart}_ET.stba.csv";
        return System.IO.Directory.EnumerateFiles(dir, stbaGlob).Any()
            && System.IO.Directory.EnumerateFiles(dir, stbaCsvGlob).Any();
    }
}
