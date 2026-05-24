using System.Text.Json.Serialization;

namespace DatabentoDbnDownloader;

internal sealed record BatchJob
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("cost_usd")] public decimal? CostUsd { get; init; }
    [JsonPropertyName("dataset")] public string? Dataset { get; init; }
    [JsonPropertyName("symbols")] public string? Symbols { get; init; }
    [JsonPropertyName("stype_in")] public string? STypeIn { get; init; }
    [JsonPropertyName("stype_out")] public string? STypeOut { get; init; }
    [JsonPropertyName("schema")] public string? Schema { get; init; }
    [JsonPropertyName("start")] public string? Start { get; init; }
    [JsonPropertyName("end")] public string? End { get; init; }
    [JsonPropertyName("encoding")] public string? Encoding { get; init; }
    [JsonPropertyName("compression")] public string? Compression { get; init; }
    [JsonPropertyName("split_symbols")] public bool? SplitSymbols { get; init; }
    [JsonPropertyName("split_duration")] public string? SplitDuration { get; init; }
    [JsonPropertyName("state")] public string State { get; init; } = "unknown";
    [JsonPropertyName("record_count")] public long? RecordCount { get; init; }
    [JsonPropertyName("billed_size")] public long? BilledSize { get; init; }
    [JsonPropertyName("actual_size")] public long? ActualSize { get; init; }
    [JsonPropertyName("package_size")] public long? PackageSize { get; init; }
    [JsonPropertyName("progress")] public int? Progress { get; init; }
}
