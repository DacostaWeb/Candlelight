using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Candlelight.Engine;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Candlelight.Next;

/// <summary>SCM-only launcher for one fixed, protected renderer in the active console session.</summary>
internal static class SecureDesktopBroker
{
    internal const string ServiceName = "Candlelight.SecureBroker";
    private static readonly ManualResetEventSlim Stop = new();
    private static readonly ServiceMainCallback MainCallback = ServiceMain;
    private static readonly ServiceHandlerCallback HandlerCallback = HandleControl;
    private static readonly object StatusLock = new();
    private static nint _statusHandle;
    private static ServiceStatus _status;

    internal static int Run()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!identity.IsSystem || Process.GetCurrentProcess().SessionId != 0)
            return 3;
        ServiceTable[] table = [new() { Name = ServiceName, Callback = MainCallback }, new()];
        return StartServiceCtrlDispatcher(table) ? 0 : 1;
    }

    private static void ServiceMain(uint count, nint arguments)
    {
        _statusHandle = RegisterServiceCtrlHandlerEx(ServiceName, HandlerCallback, 0);
        if (_statusHandle == 0)
            return;
        Report(2, 0, 10000); // START_PENDING
        uint exitCode = 0;
        try
        {
            using var job = CreateJobObject(0, null);
            if (job.IsInvalid)
                throw Error("Create renderer lifetime job");
            var limits = new ExtendedJobLimits { Basic = new() { LimitFlags = 0x2000 } }; // KILL_ON_JOB_CLOSE
            if (
                !SetInformationJobObject(
                    job,
                    9,
                    ref limits,
                    (uint)Marshal.SizeOf<ExtendedJobLimits>()
                )
            )
                throw Error("Set renderer lifetime limit");
            EnablePrivilege("SeTcbPrivilege");
            EnablePrivilege("SeAssignPrimaryTokenPrivilege");
            EnablePrivilege("SeIncreaseQuotaPrivilege");
            Report(4, 1); // RUNNING; accept STOP only
            RunSessions(job);
        }
        catch (Exception error)
        {
            exitCode = 1;
            Save(new { error = error.ToString(), timestamp = DateTimeOffset.UtcNow });
        }
        finally
        {
            Report(1, 0, exitCode: exitCode); // STOPPED
        }
    }

    private static void RunSessions(SafeFileHandle job)
    {
        Child? child = null;
        string? previousDiagnostic = null;
        try
        {
            while (!Stop.IsSet)
            {
                var session = WTSGetActiveConsoleSessionId();
                string? owner = null;
                string? error = null;
                if (session is > 0 and < int.MaxValue)
                {
                    try
                    {
                        owner = SessionAccount.UserSid((int)session);
                        if (!IsEnabled(owner))
                            owner = null;
                    }
                    catch (Exception failure)
                    {
                        error = failure.Message;
                        owner = null;
                    }
                }
                if (
                    child is not null
                    && (
                        owner != child.Owner
                        || session != child.Session
                        || WaitForSingleObject(child.Handle, 0) != 0x102
                    )
                )
                {
                    child.Dispose();
                    child = null;
                }
                if (owner is not null && child is null)
                {
                    try
                    {
                        child = Launch(session, owner, job);
                    }
                    catch (Exception failure)
                    {
                        error = failure.Message;
                    }
                }
                var diagnostic = JsonSerializer.Serialize(
                    new
                    {
                        consoleSession = session,
                        rendererPid = child?.Pid,
                        prepared = child is not null,
                        error,
                    }
                );
                if (diagnostic != previousDiagnostic)
                {
                    Save(
                        new
                        {
                            state = JsonSerializer.Deserialize<JsonElement>(diagnostic),
                            timestamp = DateTimeOffset.UtcNow,
                        }
                    );
                    previousDiagnostic = diagnostic;
                }
                Stop.Wait(1000);
            }
        }
        finally
        {
            child?.Dispose();
        }
    }

    private static bool IsEnabled(string sid)
    {
        using var key = Registry.Users.OpenSubKey(
            sid + "\\" + SecureDesktopProfiles.AccessibilityPath
        );
        var configuration = key?.GetValue("Configuration") as string;
        return configuration is { Length: <= 8192 }
            && configuration
                .Split(',', StringSplitOptions.TrimEntries)
                .Contains(SecureDesktopProfiles.Registration, StringComparer.Ordinal);
    }

    private static Child Launch(uint session, string owner, SafeFileHandle job)
    {
        // No executable, arguments, desktop name, token or session is supplied by user settings.
        var executable = Path.Combine(
            Path.GetDirectoryName(Environment.ProcessPath)!,
            "Candlelight.Secure.exe"
        );
        if (!OpenProcessToken(-1, 0x2B, out var original)) // DUPLICATE | QUERY | ASSIGN_PRIMARY | ADJUST_PRIVILEGES
            throw Error("Read broker token");
        using (original)
        {
            if (!DuplicateTokenEx(original, 0x18B, 0, 2, 1, out var token))
                throw Error("Create renderer primary token");
            using (token)
            {
                if (!SetTokenInformation(token, 12, ref session, 4)) // TokenSessionId
                    throw Error("Select console session");
                uint uiAccess = 1;
                if (!SetTokenInformation(token, 26, ref uiAccess, 4)) // Signed UIAccess renderer, SYSTEM/TCB only.
                    throw Error("Enable protected renderer UIAccess");
                var startup = new StartupInfo
                {
                    Size = (uint)Marshal.SizeOf<StartupInfo>(),
                    Desktop = @"WinSta0\Winlogon",
                };
                var command = new StringBuilder("\"" + executable + "\" --secure-desktop");
                if (
                    !CreateProcessAsUser(
                        token,
                        executable,
                        command,
                        0,
                        0,
                        false,
                        0x08000404,
                        0,
                        Path.GetDirectoryName(executable),
                        ref startup,
                        out var process
                    )
                )
                    throw Error("Prepare protected desktop renderer");
                using var thread = new SafeFileHandle(process.Thread, true);
                var handle = new SafeFileHandle(process.Process, true);
                try
                {
                    if (
                        !AssignProcessToJobObject(job, handle)
                        || ResumeThread(thread) == uint.MaxValue
                    )
                        throw Error("Attach renderer lifetime");
                    return new(handle, process.Pid, session, owner);
                }
                catch
                {
                    TerminateProcess(handle, 1);
                    handle.Dispose();
                    throw;
                }
            }
        }
    }

    private sealed record Child(SafeFileHandle Handle, uint Pid, uint Session, string Owner)
        : IDisposable
    {
        public void Dispose()
        {
            // This handle can only refer to the renderer this service created, never a supplied PID.
            if (WaitForSingleObject(Handle, 0) == 0x102)
                TerminateProcess(Handle, 0);
            Handle.Dispose();
        }
    }

    private static void EnablePrivilege(string name)
    {
        if (!OpenProcessToken(-1, 0x28, out var token))
            throw Error("Open broker privileges");
        using (token)
        {
            if (!LookupPrivilegeValue(null, name, out var privilege))
                throw Error("Resolve broker privilege");
            var privileges = new TokenPrivileges
            {
                Count = 1,
                Id = privilege,
                Attributes = 2,
            };
            if (
                !AdjustTokenPrivileges(token, false, ref privileges, 0, 0, 0)
                || Marshal.GetLastWin32Error() != 0
            )
                throw Error("Enable broker privilege");
        }
    }

    private static uint HandleControl(uint control, uint eventType, nint data, nint context)
    {
        if (control == 1)
        {
            Report(3, 0, 5000); // STOP_PENDING
            Stop.Set();
        }
        else if (control == 4)
            lock (StatusLock)
                SetServiceStatus(_statusHandle, ref _status);
        return 0;
    }

    private static void Report(uint state, uint accepted, uint waitHint = 0, uint exitCode = 0)
    {
        lock (StatusLock)
        {
            _status = new()
            {
                Type = 0x10,
                State = state,
                Accepted = accepted,
                Win32ExitCode = exitCode,
                WaitHint = waitHint,
                CheckPoint = state is 2 or 3 ? 1u : 0u,
            };
            SetServiceStatus(_statusHandle, ref _status);
        }
    }

    private static void Save(object value)
    {
        try
        {
            File.WriteAllText(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Candlelight",
                    "SecureDesktop",
                    "broker.json"
                ),
                JsonSerializer.Serialize(value)
            );
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static Win32Exception Error(string operation) =>
        new(Marshal.GetLastWin32Error(), operation);

    private delegate void ServiceMainCallback(uint count, nint arguments);
    private delegate uint ServiceHandlerCallback(
        uint control,
        uint eventType,
        nint data,
        nint context
    );

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ServiceTable
    {
        public string? Name;
        public ServiceMainCallback? Callback;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint Type,
            State,
            Accepted,
            Win32ExitCode,
            ServiceExitCode,
            CheckPoint,
            WaitHint;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint Low;
        public int High;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges
    {
        public uint Count;
        public Luid Id;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicJobLimits
    {
        public long ProcessTime,
            JobTime;
        public uint LimitFlags;
        public nuint MinimumWorkingSet,
            MaximumWorkingSet;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint Priority,
            Scheduling;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedJobLimits
    {
        public BasicJobLimits Basic;
        public ulong ReadOps,
            WriteOps,
            OtherOps,
            ReadBytes,
            WriteBytes,
            OtherBytes;
        public nuint ProcessMemory,
            JobMemory,
            PeakProcessMemory,
            PeakJobMemory;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public uint Size;
        public string? Reserved,
            Desktop,
            Title;
        public uint X,
            Y,
            Width,
            Height,
            XChars,
            YChars,
            Fill,
            Flags;
        public ushort Show,
            ReservedSize;
        public nint ReservedData,
            StdIn,
            StdOut,
            StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInfo
    {
        public nint Process,
            Thread;
        public uint Pid,
            Tid;
    }

    [DllImport("advapi32.dll", EntryPoint = "StartServiceCtrlDispatcherW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartServiceCtrlDispatcher([In] ServiceTable[] table);

    [DllImport(
        "advapi32.dll",
        EntryPoint = "RegisterServiceCtrlHandlerExW",
        CharSet = CharSet.Unicode,
        SetLastError = true
    )]
    private static extern nint RegisterServiceCtrlHandlerEx(
        string name,
        ServiceHandlerCallback callback,
        nint context
    );

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetServiceStatus(nint handle, ref ServiceStatus status);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(
        nint process,
        uint access,
        out SafeAccessTokenHandle token
    );

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateTokenEx(
        SafeAccessTokenHandle token,
        uint access,
        nint attributes,
        int level,
        int type,
        out SafeAccessTokenHandle duplicate
    );

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetTokenInformation(
        SafeAccessTokenHandle token,
        int type,
        ref uint information,
        uint length
    );

    [DllImport(
        "advapi32.dll",
        EntryPoint = "LookupPrivilegeValueW",
        CharSet = CharSet.Unicode,
        SetLastError = true
    )]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValue(string? system, string name, out Luid value);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(
        SafeAccessTokenHandle token,
        [MarshalAs(UnmanagedType.Bool)] bool disable,
        ref TokenPrivileges privileges,
        uint length,
        nint previous,
        nint required
    );

    [DllImport(
        "advapi32.dll",
        EntryPoint = "CreateProcessAsUserW",
        CharSet = CharSet.Unicode,
        SetLastError = true
    )]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessAsUser(
        SafeAccessTokenHandle token,
        string application,
        StringBuilder command,
        nint processAttributes,
        nint threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inherit,
        uint flags,
        nint environment,
        string? directory,
        ref StartupInfo startup,
        out ProcessInfo process
    );

    [DllImport(
        "kernel32.dll",
        EntryPoint = "CreateJobObjectW",
        CharSet = CharSet.Unicode,
        SetLastError = true
    )]
    private static extern SafeFileHandle CreateJobObject(nint attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        SafeFileHandle job,
        int type,
        ref ExtendedJobLimits information,
        uint length
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, SafeFileHandle process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(SafeFileHandle thread);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(SafeFileHandle handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(SafeFileHandle process, uint code);

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();
}
