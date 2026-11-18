using System.Runtime.InteropServices;
using System.Text;

namespace LANVC.Security.Credentials;

public static class WindowsCredentialManager
{
    private const int CRED_TYPE_GENERIC = 1;
    private const int CRED_PERSIST_LOCAL_MACHINE = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public int Flags;
        public int Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite([In] ref CREDENTIAL userCredential, [In] int flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredFree", SetLastError = true)]
    private static extern void CredFree([In] IntPtr credPointer);

    public static bool SavePassword(string serverAddress, string username, string password)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return false;

        string target = $"LANVC:{serverAddress}";
        byte[] secretBytes = Encoding.Unicode.GetBytes(password);
        IntPtr credentialBlobPtr = Marshal.AllocHGlobal(secretBytes.Length);

        try
        {
            Marshal.Copy(secretBytes, 0, credentialBlobPtr, secretBytes.Length);

            var cred = new CREDENTIAL
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = Marshal.StringToHGlobalUni(target),
                UserName = Marshal.StringToHGlobalUni(username),
                CredentialBlob = credentialBlobPtr,
                CredentialBlobSize = secretBytes.Length,
                Persist = CRED_PERSIST_LOCAL_MACHINE
            };

            bool success = CredWrite(ref cred, 0);

            Marshal.FreeHGlobal(cred.TargetName);
            Marshal.FreeHGlobal(cred.UserName);

            return success;
        }
        catch
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(credentialBlobPtr);
        }
    }

    public static string? LoadPassword(string serverAddress)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return null;

        string target = $"LANVC:{serverAddress}";
        if (CredRead(target, CRED_TYPE_GENERIC, 0, out IntPtr credPtr))
        {
            try
            {
                var cred = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
                if (cred.CredentialBlobSize > 0 && cred.CredentialBlob != IntPtr.Zero)
                {
                    byte[] bytes = new byte[cred.CredentialBlobSize];
                    Marshal.Copy(cred.CredentialBlob, bytes, 0, cred.CredentialBlobSize);
                    return Encoding.Unicode.GetString(bytes);
                }
            }
            finally
            {
                CredFree(credPtr);
            }
        }
        return null;
    }

    public static bool DeletePassword(string serverAddress)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return false;

        string target = $"LANVC:{serverAddress}";
        return CredDelete(target, CRED_TYPE_GENERIC, 0);
    }
}
