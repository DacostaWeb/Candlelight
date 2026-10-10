using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Candlelight.Engine;

internal static class DesktopAccess
{
    // Read the granted token, rather than inferring access from the manifest or
    // installation directory. No elevation, token mutation or private APIs.
    internal static bool UiAccessEnabled { get; } = ReadUiAccess();

    private static bool ReadUiAccess()
    {
        if (!OpenProcessToken(-1, 8, out var token)) // TOKEN_QUERY
            return false;
        using (token)
            return GetTokenInformation(token, 26, out var enabled, 4, out _) && enabled != 0;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(
        nint process,
        uint access,
        out SafeAccessTokenHandle token
    );

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        SafeAccessTokenHandle token,
        int informationClass,
        out uint information,
        uint length,
        out uint returned
    );
}
