namespace DatabentoDbnDownloader.TickSets;

public enum Source : byte
{
    DataBento = 1
}

public static class SourceExtensions
{
    public static string ToCode(this Source source) => source switch
    {
        Source.DataBento => "DB",
        _ => throw new ArgumentOutOfRangeException(nameof(source), $"Unknown source: {source}")
    };

    public static Source ParseCode(string code) => code.ToUpperInvariant() switch
    {
        "DB" => Source.DataBento,
        _ => throw new ArgumentException($"Unknown source code: {code}", nameof(code))
    };
}
