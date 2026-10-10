using System.Runtime.InteropServices;
using System.Text;

namespace Candlelight.Engine;

internal static class InputDesktop
{
    internal static string? ThreadName => Name(GetThreadDesktop(GetCurrentThreadId()));

    internal static bool IsActive(string? desktop)
    {
        if (desktop is null)
            return false;
        var input = OpenInputDesktop(0, false, 1); // DESKTOP_READOBJECTS, never switch desktops.
        if (input == 0)
            return false;
        try
        {
            return string.Equals(desktop, Name(input), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CloseDesktop(input);
        }
    }

    private static string? Name(nint desktop)
    {
        var name = new StringBuilder(256);
        return desktop != 0 && GetUserObjectInformation(desktop, 2, name, 512, out _)
            ? name.ToString()
            : null;
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern nint GetThreadDesktop(uint thread);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint OpenInputDesktop(
        uint flags,
        [MarshalAs(UnmanagedType.Bool)] bool inherit,
        uint access
    );

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseDesktop(nint desktop);

    [DllImport("user32.dll", EntryPoint = "GetUserObjectInformationW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserObjectInformation(
        nint handle,
        int index,
        StringBuilder information,
        uint length,
        out uint needed
    );
}
