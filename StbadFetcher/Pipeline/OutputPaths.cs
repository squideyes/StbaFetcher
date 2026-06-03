using System.Globalization;
using SquidEyes.Pricing;

namespace StbadFetcher;

/// <summary>
/// Resolves the on-disk layout for converted output files. All artifacts for a
/// <c>(symbol, trade-date)</c> tuple live in <c>{SaveTo}/{Symbol}/{Year}/</c>, with canonical
/// filenames produced by <see cref="PricingFile.BuildStem(Symbol, DateOnly, Contract, Source, SessionKind)"/>.
/// </summary>
internal static class OutputPaths
{
    public static string Directory(string saveTo, Symbol symbol, DateOnly date) =>
        Path.Combine(saveTo, symbol.ToString(), date.Year.ToString(CultureInfo.InvariantCulture));

    public static string StbadPath(string saveTo, Symbol symbol, DateOnly date, Contract contract, SessionKind session) =>
        Path.Combine(Directory(saveTo, symbol, date),
            PricingFile.BuildStem(symbol, date, contract, Source.DataBento, session) + ".stbad");

    /// <summary>
    /// True iff both MTH and DTH .stbad files for <paramref name="symbol"/> on
    /// <paramref name="date"/> already exist (under any contract suffix). Used to skip
    /// already-fetched <c>(symbol, date)</c> pairs without issuing a billed batch request.
    /// </summary>
    public static bool AllOutputsExist(string saveTo, Symbol symbol, DateOnly date)
    {
        var dir = Directory(saveTo, symbol, date);
        if (!System.IO.Directory.Exists(dir)) return false;

        var datePart = date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var sourcePart = Source.DataBento.ToCode();
        return SessionCovered(dir, symbol, datePart, sourcePart, SessionKind.MTH)
            && SessionCovered(dir, symbol, datePart, sourcePart, SessionKind.DTH);
    }

    private static bool SessionCovered(string dir, Symbol symbol, string datePart, string sourcePart, SessionKind session)
    {
        var glob = $"{symbol}_{datePart}_*_{sourcePart}_{session.ToCode()}_ET.stbad";
        return System.IO.Directory.EnumerateFiles(dir, glob).Any();
    }
}
