using System.Globalization;

namespace DatabentoDbnDownloader.OutputFormatters;

internal static class FormatHelpers
{
    private static readonly TimeZoneInfo Eastern = FindEastern();

    public static string FormatUtcTs(long ns)
    {
        if (ns == 0 || ns == long.MaxValue || ns == long.MinValue) return "";
        var seconds = ns / 1_000_000_000L;
        var subNs = ns - seconds * 1_000_000_000L;
        if (subNs < 0) { seconds--; subNs += 1_000_000_000L; }
        var dto = DateTimeOffset.FromUnixTimeSeconds(seconds);
        return $"{dto:yyyy-MM-ddTHH:mm:ss}.{subNs:D9}Z";
    }

    public static string FormatEtTs(long ns)
    {
        if (ns == 0 || ns == long.MaxValue || ns == long.MinValue) return "";
        var dt = ToEt(ns);
        return dt.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture);
    }

    public static DateTime ToEt(long ns)
    {
        var seconds = ns / 1_000_000_000L;
        var subNs = ns - seconds * 1_000_000_000L;
        if (subNs < 0) { seconds--; subNs += 1_000_000_000L; }
        var utc = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
        utc = utc.AddTicks(subNs / 100);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, Eastern);
    }

    public static string FormatPx(long fixedPx)
    {
        if (fixedPx == long.MaxValue || fixedPx == long.MinValue) return "";
        var sign = fixedPx < 0 ? "-" : "";
        var abs = fixedPx < 0 ? -fixedPx : fixedPx;
        var whole = abs / 1_000_000_000L;
        var frac = abs % 1_000_000_000L;
        if (frac == 0) return $"{sign}{whole.ToString(CultureInfo.InvariantCulture)}";
        var fracStr = frac.ToString("D9", CultureInfo.InvariantCulture).TrimEnd('0');
        return $"{sign}{whole.ToString(CultureInfo.InvariantCulture)}.{fracStr}";
    }

    public static decimal PxToDecimal(long fixedPx) =>
        fixedPx == long.MaxValue || fixedPx == long.MinValue
            ? 0m
            : (decimal)fixedPx / 1_000_000_000m;

    private static TimeZoneInfo FindEastern()
    {
        foreach (var id in new[] { "Eastern Standard Time", "America/New_York" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        throw new InvalidOperationException("Could not find Eastern time zone.");
    }
}
