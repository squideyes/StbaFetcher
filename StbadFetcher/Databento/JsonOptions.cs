using System.Text.Json;

namespace StbadFetcher;

internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
}
