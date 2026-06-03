using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace StbadFetcher;

internal static class DatabentoHttpClient
{
    public static HttpClient Create(string apiKey)
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        })
        {
            BaseAddress = new Uri("https://hist.databento.com/v0/"),
            // Streaming MBP-1 responses can be hundreds of MB and take several minutes;
            // per-request cancellation is enforced via the caller's CancellationToken.
            Timeout = Timeout.InfiniteTimeSpan,
        };

        var raw = Encoding.ASCII.GetBytes($"{apiKey}:");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(raw));
        client.DefaultRequestHeaders.UserAgent.ParseAdd("StbadFetcher/1.0");
        return client;
    }
}
