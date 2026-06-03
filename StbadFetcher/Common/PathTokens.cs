using System.Text.RegularExpressions;

namespace StbadFetcher;

internal static partial class PathTokens
{
    private static readonly Dictionary<string, Func<string>> _resolvers =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["MYDOCS"] = () => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            ["DESKTOP"] = () => Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            ["USERPROFILE"] = () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ["LOCALAPPDATA"] = () => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        };

    public static IReadOnlyList<string> Known { get; } =
        ["MYDOCS", "DESKTOP", "USERPROFILE", "LOCALAPPDATA"];

    [GeneratedRegex(@"%(?<name>[A-Za-z][A-Za-z0-9_]*)%")]
    private static partial Regex TokenRegex();

    public static string Expand(string input)
    {
        if (string.IsNullOrEmpty(input) || !input.Contains('%'))
            return input;

        var afterTokens = TokenRegex().Replace(input, match =>
        {
            var name = match.Groups["name"].Value;
            return _resolvers.TryGetValue(name, out var resolver)
                ? resolver()
                : match.Value;
        });

        var afterEnv = Environment.ExpandEnvironmentVariables(afterTokens);

        var leftover = TokenRegex().Match(afterEnv);
        if (leftover.Success)
        {
            var knownList = string.Join(", ", Known.Select(k => "%" + k + "%"));
            throw new ArgumentException(
                $"Unknown path token '{leftover.Value}' in '{input}'. " +
                $"Known tokens: {knownList}, or any defined environment variable.");
        }

        return afterEnv;
    }
}
