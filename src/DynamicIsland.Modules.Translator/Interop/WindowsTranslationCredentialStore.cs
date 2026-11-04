using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using DynamicIsland.Modules.Translator.Services;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace DynamicIsland.Modules.Translator.Interop;

/// <summary>Generic Windows credential; encryption and user isolation are managed by Windows.</summary>
internal sealed class WindowsTranslationCredentialStore : ITranslationCredentialStore
{
    private const string Target = "DynamicIsland/YandexTranslate/v1";
    private const uint Generic = 1;
    private const int NotFound = 1168;

    public TranslationCredentials? Read()
    {
        if (!CredRead(Target, Generic, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == NotFound)
            {
                return null;
            }

            throw new Win32Exception(error);
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            if (credential.BlobSize is < 2 or > 2560 || credential.Blob == 0)
            {
                return null;
            }

            var bytes = new byte[credential.BlobSize];
            Marshal.Copy(credential.Blob, bytes, 0, bytes.Length);
            try
            {
                return new TranslationCredentials(Encoding.UTF8.GetString(bytes, 1, bytes.Length - 1), bytes[0] == 1);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public void Write(TranslationCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        if (string.IsNullOrWhiteSpace(credentials.ApiKey) || credentials.ApiKey.Length > 2048 || credentials.ApiKey.Any(char.IsControl))
        {
            throw new ArgumentException("Invalid API key.", nameof(credentials));
        }

        var bytes = Encoding.UTF8.GetBytes((credentials.Consent ? "\u0001" : "\0") + credentials.ApiKey);
        if (bytes.Length > 2560)
        {
            CryptographicOperations.ZeroMemory(bytes);
            throw new ArgumentException("API key is too long.", nameof(credentials));
        }

        var blob = Marshal.AllocCoTaskMem(bytes.Length);
        var name = Marshal.StringToCoTaskMemUni(Target);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new Credential
            {
                Type = Generic,
                TargetName = name,
                BlobSize = (uint)bytes.Length,
                Blob = blob,
                Persist = 2, // CRED_PERSIST_LOCAL_MACHINE: same user, survives sign-out; no roaming.
            };
            if (!CredWrite(ref credential, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            Marshal.FreeCoTaskMem(blob);
            Marshal.FreeCoTaskMem(name);
        }
    }

    public void Delete()
    {
        if (!CredDelete(Target, Generic, 0) && Marshal.GetLastWin32Error() is var error && error != NotFound)
        {
            throw new Win32Exception(error);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public nint TargetName;
        public nint Comment;
        public long LastWritten;
        public uint BlobSize;
        public nint Blob;
        public uint Persist;
        public uint AttributeCount;
        public nint Attributes;
        public nint TargetAlias;
        public nint UserName;
    }

    // The Windows credential structure contains pointers owned by CredFree, rather than managed fields.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "SYSLIB1054", Justification = "Explicit native CREDENTIALW pointer marshalling.")]
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out nint credential);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "SYSLIB1054", Justification = "Explicit native CREDENTIALW pointer marshalling.")]
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref Credential credential, uint flags);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "SYSLIB1054", Justification = "Explicit native CREDENTIALW pointer marshalling.")]
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "SYSLIB1054", Justification = "Explicit native CREDENTIALW pointer marshalling.")]
    [DllImport("advapi32.dll", ExactSpelling = true)]
    private static extern void CredFree(nint credential);
}
