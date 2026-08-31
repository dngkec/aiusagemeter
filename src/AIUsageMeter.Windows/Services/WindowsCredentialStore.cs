using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using AIUsageMeter.Core;

namespace AIUsageMeter.Windows.Services;

internal sealed class WindowsCredentialStore : ISecretStore
{
    private const string Prefix = "AIUsageMeter/";
    private const uint Generic = 1;
    private const uint PersistLocalMachine = 2;

    public string? Read(string account)
    {
        if (!CredRead(Prefix + account, Generic, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == 1168) return null;
            throw new Win32Exception(error, "Windows Credential Manager could not read the app-owned secret.");
        }
        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0) return null;
            return Marshal.PtrToStringUni(credential.CredentialBlob, checked((int)credential.CredentialBlobSize / 2));
        }
        finally { CredFree(pointer); }
    }

    public void Write(string account, string? value)
    {
        var target = Prefix + account;
        if (string.IsNullOrEmpty(value))
        {
            if (!CredDelete(target, Generic, 0) && Marshal.GetLastWin32Error() != 1168)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows Credential Manager could not remove the app-owned secret.");
            return;
        }
        var bytes = Encoding.Unicode.GetBytes(value);
        if (bytes.Length > 2_560) throw new ArgumentOutOfRangeException(nameof(value), "The secret is too large for Windows Credential Manager.");
        var blob = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new NativeCredential
            {
                Type = Generic, TargetName = target, CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob, Persist = PersistLocalMachine, UserName = Environment.UserName
            };
            if (!CredWrite(ref credential, 0)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows Credential Manager could not save the app-owned secret.");
        }
        finally
        {
            Marshal.Copy(new byte[bytes.Length], 0, blob, bytes.Length);
            Marshal.FreeCoTaskMem(blob);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags; public uint Type; public string TargetName; public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten; public uint CredentialBlobSize;
        public IntPtr CredentialBlob; public uint Persist; public uint AttributeCount; public IntPtr Attributes;
        public string? TargetAlias; public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredWrite(ref NativeCredential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredDelete(string target, uint type, uint flags);
    [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr credential);
}
