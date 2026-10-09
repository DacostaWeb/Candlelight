using System;
using System.IO;

namespace LightBulb.Diagnostics;

internal static class ColorTrace
{
    private static readonly object Sync = new();
    private static readonly string FilePath = Path.Combine(
        Program.ExecutableDirPath,
        "ColorStatus.txt"
    );

    public static void Write(string text)
    {
        if (StartOptions.Current.IsPreview)
            return;
        lock (Sync)
        {
            try
            {
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 128 * 1024)
                    File.WriteAllText(FilePath, "");
                File.AppendAllText(FilePath, $"{DateTimeOffset.Now:O} {text}\n");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Diagnostics must never interrupt color application.
            }
        }
    }
}
