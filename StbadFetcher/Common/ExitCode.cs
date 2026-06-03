namespace StbadFetcher;

/// <summary>Process exit codes returned by <c>Program.Main</c>.</summary>
internal static class ExitCode
{
    /// <summary>Normal completion.</summary>
    public const int Success = 0;

    /// <summary>Unhandled or otherwise fatal error.</summary>
    public const int Error = 1;

    /// <summary>Malformed or missing command-line arguments.</summary>
    public const int BadArguments = 2;

    /// <summary>Caller cancelled (Ctrl+C). Conventional POSIX value 128 + SIGINT(2).</summary>
    public const int Cancelled = 130;
}
