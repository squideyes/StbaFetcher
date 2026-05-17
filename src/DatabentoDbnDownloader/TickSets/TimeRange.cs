namespace DatabentoDbnDownloader.TickSets;

public enum TimeRange : byte
{
    DTH = 1,   // Day Trading Hours       08:00–16:00 ET
    MTH        // Morning Trading Hours   08:00–12:00 ET
}

public static class TimeRangeExtensions
{
    public static (TimeOnly From, TimeOnly Until) ToTimes(this TimeRange range) => range switch
    {
        TimeRange.DTH => (new(8, 0),  new(16, 0)),
        TimeRange.MTH => (new(8, 0),  new(12, 0)),
        _ => throw new ArgumentOutOfRangeException(nameof(range), $"Unknown TimeRange: {range}")
    };

    public static string ToCode(this TimeRange range) => range switch
    {
        TimeRange.DTH => "DTH",
        TimeRange.MTH => "MTH",
        _ => throw new ArgumentOutOfRangeException(nameof(range), $"Unknown TimeRange: {range}")
    };

    public static TimeRange ParseCode(string code) => code.ToUpperInvariant() switch
    {
        "DTH" => TimeRange.DTH,
        "MTH" => TimeRange.MTH,
        _ => throw new ArgumentException($"Unknown TimeRange code: {code}", nameof(code))
    };
}
