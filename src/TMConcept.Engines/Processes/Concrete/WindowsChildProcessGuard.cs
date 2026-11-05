using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace TMConcept.Engines.Processes;

/// <summary>
/// Windows: one job object with JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE for the app's lifetime. Every child is assigned to it right after it
/// starts and the processes it starts (Node → Chrome) join it by themselves. Only the app holds the job's handle, so when the app ends in
/// any way (closed, killed, crashed) Windows closes the handle and ends every process in the job. Nested jobs (Windows 8+) let this work
/// when the app itself runs inside a job (a terminal, an IDE). For the start-up sweep a process is known by its executable path
/// (QueryFullProcessImageName, never its arguments) and its parent id (NtQueryInformationProcess).
/// </summary>
public sealed partial class WindowsChildProcessGuard : ChildProcessGuardBase
{
    private const uint KillOnJobClose = 0x2000;
    private const int ExtendedLimitInformation = 9;
    private const uint QueryLimitedInformation = 0x1000;
    private readonly ILogger<WindowsChildProcessGuard> _logger;
    private IntPtr _job;

    /// <summary>Creates the job (logged when Windows refuses; children are then still ended on exit).</summary>
    public WindowsChildProcessGuard(ILogger<WindowsChildProcessGuard> logger)
        : base(logger)
    {
        _logger = logger;
        _job = CreateJobObject(IntPtr.Zero, null);
        if (_job == IntPtr.Zero)
        {
            _logger.LogError("The job object for child processes could not be created (Windows error {Error})", Marshal.GetLastPInvokeError());
            return;
        }

        var limits = new ExtendedLimits { Basic = new BasicLimits { LimitFlags = KillOnJobClose } };
        if (!SetInformationJobObject(_job, ExtendedLimitInformation, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>()))
        {
            _logger.LogError("The job object for child processes could not be set up (Windows error {Error})", Marshal.GetLastPInvokeError());
            CloseHandle(_job);
            _job = IntPtr.Zero;
        }
    }

    /// <inheritdoc />
    protected override StringComparison PathComparison => StringComparison.OrdinalIgnoreCase;

    /// <inheritdoc />
    internal override bool IsTied(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        return _job != IntPtr.Zero && IsProcessInJob(process.Handle, _job, out var inJob) && inJob;
    }

    /// <inheritdoc />
    protected override void Tie(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (_job == IntPtr.Zero)
        {
            throw new InvalidOperationException("There is no job object.");
        }

        if (!AssignProcessToJobObject(_job, process.Handle))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    /// <inheritdoc />
    protected override string? ImagePath(int pid)
    {
        var handle = OpenProcess(QueryLimitedInformation, false, (uint)pid);
        if (handle == IntPtr.Zero)
        {
            // Another user's or a protected process, or it ended: not ours.
            return null;
        }

        try
        {
            var buffer = new char[32_768];
            var length = (uint)buffer.Length;
            return QueryFullProcessImageName(handle, 0, buffer, ref length) ? new string(buffer, 0, (int)length) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <inheritdoc />
    protected override int? ParentId(int pid)
    {
        var handle = OpenProcess(QueryLimitedInformation, false, (uint)pid);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var info = default(BasicInformation);
            return NtQueryInformationProcess(handle, 0, ref info, (uint)Marshal.SizeOf<BasicInformation>(), out _) == 0
                ? (int)info.InheritedFromUniqueProcessId
                : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }
    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (_job != IntPtr.Zero)
        {
            // Closing the last handle ends every process still in the job.
            CloseHandle(_job);
            _job = IntPtr.Zero;
        }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial IntPtr CreateJobObject(IntPtr attributes, string? name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(IntPtr job, int informationClass, ref ExtendedLimits information, uint length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsProcessInJob(IntPtr process, IntPtr job, [MarshalAs(UnmanagedType.Bool)] out bool result);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryFullProcessImageName(IntPtr process, uint flags, [Out] char[] name, ref uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);

    [LibraryImport("ntdll.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int NtQueryInformationProcess(IntPtr process, int informationClass, ref BasicInformation information, uint length,
        out uint returned);

    /// <summary>PROCESS_BASIC_INFORMATION (only the parent id is read).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct BasicInformation
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public nuint UniqueProcessId;
        public nuint InheritedFromUniqueProcessId;
    }

    /// <summary>JOBOBJECT_BASIC_LIMIT_INFORMATION.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    /// <summary>IO_COUNTERS.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    /// <summary>JOBOBJECT_EXTENDED_LIMIT_INFORMATION.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits Basic;
        public IoCounters Io;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }
}
