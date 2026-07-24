using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using QuotaDock.Core.Abstractions;

namespace QuotaDock.Infrastructure.Security;

public sealed partial class WindowsCredentialVault : ISecretVault
{
    private const uint CredentialTypeGeneric = 1;
    private const uint CredentialPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    private const int MaximumBlobBytes = 2560;

    private readonly string targetPrefix;

    public WindowsCredentialVault(string applicationNamespace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationNamespace);
        if (!SafeNamespace().IsMatch(applicationNamespace))
        {
            throw new ArgumentException("Credential namespace contains unsupported characters.", nameof(applicationNamespace));
        }

        targetPrefix = applicationNamespace;
    }

    public ValueTask SaveAsync(
        string reference,
        string secret,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateReference(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        var secretBytes = Encoding.UTF8.GetBytes(secret);
        if (secretBytes.Length > MaximumBlobBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(secret), "Credential is too large for Windows Credential Manager.");
        }

        var blob = Marshal.AllocCoTaskMem(secretBytes.Length);
        try
        {
            Marshal.Copy(secretBytes, 0, blob, secretBytes.Length);
            var credential = new NativeCredential
            {
                Type = CredentialTypeGeneric,
                TargetName = Target(reference),
                CredentialBlobSize = (uint)secretBytes.Length,
                CredentialBlob = blob,
                Persist = CredentialPersistLocalMachine,
                UserName = "QuotaDock"
            };

            if (!CredWrite(ref credential, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows Credential Manager rejected the credential.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secretBytes);
            Marshal.FreeCoTaskMem(blob);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<string?> RetrieveAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateReference(reference);
        if (!CredRead(Target(reference), CredentialTypeGeneric, 0, out var credentialPointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound)
            {
                return ValueTask.FromResult<string?>(null);
            }

            throw new Win32Exception(error, "Windows Credential Manager could not read the credential.");
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
            {
                return ValueTask.FromResult<string?>(string.Empty);
            }

            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            try
            {
                return ValueTask.FromResult<string?>(Encoding.UTF8.GetString(bytes));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    public ValueTask RemoveAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateReference(reference);
        if (!CredDelete(Target(reference), CredentialTypeGeneric, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ErrorNotFound)
            {
                throw new Win32Exception(error, "Windows Credential Manager could not remove the credential.");
            }
        }

        return ValueTask.CompletedTask;
    }

    private string Target(string reference) => $"{targetPrefix}/{reference}";

    private static void ValidateReference(string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        if (!SafeReference().IsMatch(reference))
        {
            throw new ArgumentException("Credential reference contains unsupported characters.", nameof(reference));
        }
    }

    [GeneratedRegex("^[A-Za-z0-9._-]{1,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeReference();

    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeNamespace();

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite([In] ref NativeCredential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(
        string target,
        uint type,
        uint reservedFlag,
        out IntPtr credentialPointer);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll", SetLastError = false)]
    private static extern void CredFree(IntPtr buffer);
}
