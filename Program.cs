using StbaFetcher;
using Microsoft.Extensions.Logging;

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
        return ExitCode.Success;
    }

    if (settings.SetApiKey is not null)
    {
        SecretStore.WriteApiKey(settings.SetApiKey);
        Console.WriteLine($"Saved {SecretStore.ApiKeyName} to Windows Credential Manager ({SecretStore.TargetName}).");
        return ExitCode.Success;
    }

    var apiKey = SecretStore.ReadApiKey();
    if (apiKey is null)
    {
        Console.Error.WriteLine("Databento API key is not set. Run:");
        Console.Error.WriteLine("  StbaFetcher --set-key db-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxx");
        return ExitCode.Error;
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
    return ExitCode.BadArguments;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled.");
    return ExitCode.Cancelled;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return ExitCode.Error;
}
