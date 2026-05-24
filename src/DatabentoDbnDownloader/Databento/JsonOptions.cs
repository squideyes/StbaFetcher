using System.Text.Json;

namespace DatabentoDbnDownloader;

internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
}
