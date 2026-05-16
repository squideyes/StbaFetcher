using System.Text.Json.Serialization;

internal sealed record BatchFileUrls
{
    [JsonPropertyName("https")] public string? Https { get; init; }
    [JsonPropertyName("ftp")] public string? Ftp { get; init; }
}
