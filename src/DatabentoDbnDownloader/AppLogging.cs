using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

internal static class AppLogging
{
    public static ILoggerFactory Factory { get; private set; } = NullLoggerFactory.Instance;

    public static void Init(LogLevel minimumLevel)
    {
        Factory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(minimumLevel);
            builder.AddSimpleConsole(opts =>
            {
                opts.SingleLine = true;
                opts.IncludeScopes = false;
                opts.TimestampFormat = "HH:mm:ss.fff ";
                opts.UseUtcTimestamp = false;
                opts.ColorBehavior = LoggerColorBehavior.Enabled;
            });
        });
    }

    public static ILogger<T> CreateLogger<T>() => Factory.CreateLogger<T>();
}
