using System.Text.Json.Serialization;

namespace StbaFetcher;

internal sealed record BatchFile
{
    [JsonPropertyName("filename")] public required string Filename { get; init; }
    [JsonPropertyName("size")] public long? Size { get; init; }
    [JsonPropertyName("hash")] public string? Hash { get; init; }
    [JsonPropertyName("urls")] public BatchFileUrls? Urls { get; init; }
}
