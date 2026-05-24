using DatabentoDbnDownloader;
using Microsoft.Extensions.Logging;

// Exit codes: 0 = success, 1 = error, 2 = bad arguments, 130 = cancelled.

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    Console.Error.WriteLine("Cancellation requested...");
    cancellation.Cancel();
};

try
{
    var settings = Settings.Parse(args);

    if (settings.ShowHelp)
    {
        Console.WriteLine(Settings.HelpText);
        return 0;
    }

    if (settings.SetApiKey is not null)
    {
        SecretStore.WriteApiKey(settings.SetApiKey);
        Console.WriteLine($"Saved {SecretStore.ApiKeyName} to {SecretStore.SecretsFilePath}");
        return 0;
    }

    var apiKey = SecretStore.ReadApiKey();
    if (apiKey is null)
    {
        Console.Error.WriteLine("Databento API key is not set. Run:");
        Console.Error.WriteLine("  DatabentoDbnDownloader --set-key db-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxx");
        return 1;
    }

    AppLogging.Init(settings.Verbose ? LogLevel.Debug : LogLevel.Information);
    var logger = AppLogging.CreateLogger("app");

    using var httpClient = DatabentoHttpClient.Create(apiKey);
    var api = new DatabentoBatchApi(httpClient, AppLogging.CreateLogger<DatabentoBatchApi>());

    var pipeline = new TickDataDownloader(api, settings, logger);
    return await pipeline.RunAsync(cancellation.Token);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine($"Argument error: {ex.Message}");
    return 2;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled.");
    return 130;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}
