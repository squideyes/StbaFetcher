using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace StbaFetcher;

/// <summary>
/// Reads/writes the Databento API key in <b>Windows Credential Manager</b> (Generic
/// credential, <c>CRED_PERSIST_LOCAL_MACHINE</c>). The blob is DPAPI-encrypted by the OS
/// and bound to the current Windows user — nothing is written to disk by this app.
/// Show up in Control Panel → Credential Manager → Windows Credentials as
/// <c>StbaFetcher:DATABENTO_API_KEY</c>.
/// </summary>
internal static class SecretStore
{
    public const string ApiKeyName = "DATABENTO_API_KEY";
    public const string TargetName = "StbaFetcher:DATABENTO_API_KEY";

    public static string? ReadApiKey()
    {
        if (!CredRead(TargetName, CRED_TYPE_GENERIC, 0, out var ptr))
        {
            var err = Marshal.GetLastWin32Error();
            if (err == ERROR_NOT_FOUND)
                return null;
            throw new Win32Exception(err, $"CredRead failed for '{TargetName}'.");
        }

        try
        {
            var cred = Marshal.PtrToStructure<CREDENTIAL>(ptr);
            if (cred.CredentialBlobSize == 0 || cred.CredentialBlob == IntPtr.Zero)
                return string.Empty;

            var bytes = new byte[cred.CredentialBlobSize];
            Marshal.Copy(cred.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.Unicode.GetString(bytes);
        }
        finally
        {
            CredFree(ptr);
        }
    }

    public static void WriteApiKey(string apiKey)
    {
        var blob = Encoding.Unicode.GetBytes(apiKey);
        var blobPtr = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, blobPtr, blob.Length);
            var cred = new CREDENTIAL
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = TargetName,
                CredentialBlobSize = blob.Length,
                CredentialBlob = blobPtr,
                Persist = CRED_PERSIST_LOCAL_MACHINE,
                UserName = Environment.UserName,
            };

            if (!CredWrite(ref cred, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    $"CredWrite failed for '{TargetName}'.");
        }
        finally
        {
            Marshal.FreeHGlobal(blobPtr);
        }
    }

    public static bool DeleteApiKey()
    {
        if (CredDelete(TargetName, CRED_TYPE_GENERIC, 0))
            return true;

        var err = Marshal.GetLastWin32Error();
        if (err == ERROR_NOT_FOUND)
            return false;
        throw new Win32Exception(err, $"CredDelete failed for '{TargetName}'.");
    }

    private const int CRED_TYPE_GENERIC = 1;
    private const int CRED_PERSIST_LOCAL_MACHINE = 2;
    private const int ERROR_NOT_FOUND = 1168;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public int Flags;
        public int Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
    }

#pragma warning disable SYSLIB1054 // CREDENTIAL marshalling needs the classic DllImport path
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref CREDENTIAL credential, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll", SetLastError = false)]
    private static extern void CredFree(IntPtr buffer);
#pragma warning restore SYSLIB1054
}
