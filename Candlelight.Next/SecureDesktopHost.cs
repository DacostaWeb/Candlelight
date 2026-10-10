using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using Candlelight.Engine;

namespace Candlelight.Next;

/// <summary>Windows launches this alternate renderer through AT registration, never our control pipe.</summary>
internal static class SecureDesktopHost
{
    internal static int Run()
    {
        // Fail closed if manually launched on the ordinary desktop or under a user token.
        using var identity = WindowsIdentity.GetCurrent();
        if (
            !identity.IsSystem
            || !string.Equals(
                InputDesktop.ThreadName,
                "Winlogon",
                StringComparison.OrdinalIgnoreCase
            )
        )
            return 3;
        using var mutex = new Mutex(
            true,
            @"Local\Candlelight.Secure." + Process.GetCurrentProcess().SessionId,
            out var first
        );
        if (!first)
            return 0;
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Candlelight",
            "SecureDesktop"
        );
        var diagnostic = Path.Combine(
            directory,
            "session-" + Process.GetCurrentProcess().SessionId + ".json"
        );
        void Save(object value)
        {
            try
            {
                File.WriteAllText(diagnostic, JsonSerializer.Serialize(value));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        try
        {
            var profiles = SecureDesktopProfiles.Read();
            // Local-only rendering avoids shared fullscreen ownership with the normal instance.
            // No cursor leases, user files, UI, pipes, plugins or input simulation on Winlogon.
            using var engine = new MagnificationEngine(
                allowDesktopEffect: false,
                manageSystemCursors: false,
                protectSessionLock: false,
                trackInputDesktop: true
            );
            engine.Ready.GetAwaiter().GetResult();
            var desktop = InputDesktop.ThreadName;
            bool? previouslyActive = null;
            EngineSnapshot? last = null;
            do
            {
                var active = InputDesktop.IsActive(desktop);
                if (active && previouslyActive == false)
                    profiles = SecureDesktopProfiles.Read();
                if (active)
                {
                    var now = TimeOnly.FromDateTime(DateTime.Now);
                    engine
                        .ApplyProfilesAsync(
                            profiles.Select(p => (p.Id, p.Evaluate(now), p.Enabled))
                        )
                        .GetAwaiter()
                        .GetResult();
                    last = engine.InspectAsync().GetAwaiter().GetResult();
                }
                if (active || previouslyActive != active)
                    Save(
                        new
                        {
                            secureDesktop = desktop,
                            identity = "SYSTEM",
                            pid = Environment.ProcessId,
                            active,
                            timestamp = DateTimeOffset.UtcNow,
                            engine = last,
                        }
                    );
                previouslyActive = active;
                Thread.Sleep(active ? 250 : 100);
            } while (true);
        }
        catch (Exception error)
        {
            Save(new { error = error.ToString(), timestamp = DateTimeOffset.UtcNow });
            return 1;
        }
    }
}
