internal static class EasternTradingWindow
{
    private static readonly TimeZoneInfo Eastern = FindEastern();

    public static (DateTimeOffset StartUtc, DateTimeOffset EndUtc) ToUtc(DateOnly date, TimeOnly startEt, TimeOnly endEt)
    {
        var startLocal = date.ToDateTime(startEt, DateTimeKind.Unspecified);
        var endLocal = date.ToDateTime(endEt, DateTimeKind.Unspecified);
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(startLocal, Eastern);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(endLocal, Eastern);
        return (new DateTimeOffset(startUtc, TimeSpan.Zero), new DateTimeOffset(endUtc, TimeSpan.Zero));
    }

    private static TimeZoneInfo FindEastern()
    {
        foreach (var id in new[] { "Eastern Standard Time", "America/New_York" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        throw new InvalidOperationException("Could not find Eastern time zone. Install tzdata in Linux containers, or run on Windows.");
    }
}
