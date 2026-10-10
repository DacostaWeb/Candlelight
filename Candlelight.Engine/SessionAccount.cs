using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Candlelight.Engine;

internal static class SessionAccount
{
    internal static string UserSid(int sessionId)
    {
        var user = Query(sessionId, 5); // WTSUserName
        var domain = Query(sessionId, 7); // WTSDomainName
        if (string.IsNullOrWhiteSpace(user))
            throw new InvalidDataException("The interactive session has no signed-in user.");
        var account = string.IsNullOrEmpty(domain)
            ? new NTAccount(user)
            : new NTAccount(domain, user);
        return ((SecurityIdentifier)account.Translate(typeof(SecurityIdentifier))).Value;
    }

    private static string Query(int sessionId, int information)
    {
        if (!WTSQuerySessionInformation(0, sessionId, information, out var buffer, out var bytes))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            if (buffer == 0 || bytes is < 2 or > 2048 || bytes % 2 != 0)
                throw new InvalidDataException("Invalid Windows session account information.");
            return (Marshal.PtrToStringUni(buffer, (int)bytes / 2) ?? "").TrimEnd('\0');
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    [DllImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQuerySessionInformation(
        nint server,
        int session,
        int information,
        out nint buffer,
        out uint bytes
    );

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(nint buffer);
}
