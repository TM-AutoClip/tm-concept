using System.Globalization;
using System.Runtime.InteropServices;
using TMConcept.Core.Hardware;

namespace TMConcept.Engines.Hardware;

/// <summary>
/// Free main memory: on Windows <c>GlobalMemoryStatusEx</c>'s available physical memory (kernel32 from System32), on Linux <c>MemAvailable</c>
/// of <c>/proc/meminfo</c> (kB). Null when neither can be read.
/// </summary>
public sealed partial class SystemMemory : ISystemMemory
{
    /// <inheritdoc />
    public long? AvailableBytes()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
                return GlobalMemoryStatusEx(ref status) ? (long)Math.Min(status.AvailablePhysical, long.MaxValue) : null;
            }

            return OperatingSystem.IsLinux() && File.Exists("/proc/meminfo") ? Available(File.ReadLines("/proc/meminfo")) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>The <c>MemAvailable</c> line of <c>/proc/meminfo</c> in bytes (null when there is none).</summary>
    public static long? Available(IEnumerable<string> meminfo)
    {
        ArgumentNullException.ThrowIfNull(meminfo);
        foreach (var line in meminfo)
        {
            if (!line.StartsWith("MemAvailable:", StringComparison.Ordinal))
            {
                continue;
            }

            var value = line["MemAvailable:".Length..].Trim();
            var number = value.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            return long.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out var kilobytes) ? kilobytes * 1024 : null;
        }

        return null;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MemoryStatus buffer);

    /// <summary>MEMORYSTATUSEX (64 bytes).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }
}
