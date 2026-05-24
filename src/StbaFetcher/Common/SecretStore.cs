using System.Security.Cryptography;
using System.Text;

namespace DatabentoDbnDownloader;

/// <summary>
/// Reads and writes the Databento API key using <see cref="ProtectedData"/> (DPAPI). The
/// ciphertext lives at <c>%LOCALAPPDATA%\DatabentoDbnDownloader\api-key.dat</c> and is bound
/// to both the current Windows user and the machine.
/// </summary>
internal static class SecretStore
{
    public const string ApiKeyName = "DATABENTO_API_KEY";

    private static readonly byte[] _entropy = "DatabentoDbnDownloader:DATABENTO_API_KEY:v1"u8.ToArray();

    public static string SecretsFilePath { get; internal set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DatabentoDbnDownloader",
        "api-key.dat");

    public static string? ReadApiKey()
    {
        if (!File.Exists(SecretsFilePath))
            return null;

        byte[] plain;
        try
        {
            var cipher = File.ReadAllBytes(SecretsFilePath);
            plain = ProtectedData.Unprotect(cipher, _entropy, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException(
                $"Could not decrypt the API-key file at '{SecretsFilePath}'. " +
                "It may be corrupted or written by a different Windows account / machine. " +
                "Re-save with: --set-key db-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxx",
                ex);
        }

        return Encoding.UTF8.GetString(plain);
    }

    public static void WriteApiKey(string apiKey)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SecretsFilePath)!);

        var plain = Encoding.UTF8.GetBytes(apiKey);
        var cipher = ProtectedData.Protect(plain, _entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(SecretsFilePath, cipher);
    }
}
