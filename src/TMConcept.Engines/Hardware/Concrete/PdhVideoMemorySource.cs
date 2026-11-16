using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Hardware;

namespace TMConcept.Engines.Hardware;

/// <summary>
/// Windows: the graphics card memory every program uses together, from the performance counter <c>\GPU Adapter Memory(*)\Dedicated Usage</c>
/// (what Task Manager shows; the same for AMD, NVIDIA and Intel). The English counter name is used (<c>PdhAddEnglishCounterW</c>): the
/// counters are named in the Windows language. Its instances are named by the card's LUID (<c>luid_0x…_0x…_phys_0</c>), which is the
/// card's <see cref="GpuAdapter.Key"/> from DXGI; free = the card's memory − what all instances of that LUID use. No WMI, no COM; pdh.dll only
/// from System32. Counters switched off on a computer (<c>lodctr /R</c> repairs them) give nothing, and DXGI's budget is used alone.
/// </summary>
public sealed partial class PdhVideoMemorySource(ILogger<PdhVideoMemorySource> logger) : IVideoMemorySource
{
    private const string Counter = @"\GPU Adapter Memory(*)\Dedicated Usage";
    private const uint MoreData = 0x800007D2;
    private const uint FormatLarge = 0x00000400;

    /// <inheritdoc />
    public string Name => "PDH";

    /// <inheritdoc />
    public Task<long?> FreeBytesAsync(GpuAdapter adapter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        if (!OperatingSystem.IsWindows() || adapter.Key.Length == 0 || adapter.VideoMemoryBytes is not { } total)
        {
            return Task.FromResult<long?>(null);
        }

        try
        {
            var used = Used(adapter.Key);
            return Task.FromResult<long?>(used is { } bytes ? Math.Max(0, total - bytes) : null);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or SEHException)
        {
            logger.LogWarning(ex, "The graphics card memory counter could not be read");
            return Task.FromResult<long?>(null);
        }
    }

    /// <summary>
    /// What the instances of <paramref name="luid"/> use together (an instance per physical part: <c>_phys_0</c>, <c>_phys_1</c>); null when the
    /// counter cannot be read or names no such instance.
    /// </summary>
    private static unsafe long? Used(string luid)
    {
        if (PdhOpenQueryW(null, 0, out var query) != 0)
        {
            return null;
        }

        try
        {
            if (PdhAddEnglishCounterW(query, Counter, 0, out var counter) != 0 || PdhCollectQueryData(query) != 0)
            {
                return null;
            }

            uint size = 0;
            var status = PdhGetFormattedCounterArrayW(counter, FormatLarge, ref size, out _, 0);
            if (status != MoreData || size == 0)
            {
                return null;
            }

            var buffer = Marshal.AllocHGlobal((nint)size);
            try
            {
                if (PdhGetFormattedCounterArrayW(counter, FormatLarge, ref size, out var count, buffer) != 0)
                {
                    return null;
                }

                long? sum = null;
                var items = (CounterItem*)buffer;
                for (var i = 0; i < count; i++)
                {
                    var name = Marshal.PtrToStringUni(items[i].Name) ?? string.Empty;
                    if (items[i].Status <= 1 && name.StartsWith(luid + "_", StringComparison.OrdinalIgnoreCase))
                    {
                        sum = (sum ?? 0) + Math.Max(0, items[i].Value);
                    }
                }

                return sum;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            _ = PdhCloseQuery(query);
        }
    }

    [LibraryImport("pdh.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint PdhOpenQueryW(string? dataSource, nint userData, out nint query);

    [LibraryImport("pdh.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint PdhAddEnglishCounterW(nint query, string counterPath, nint userData, out nint counter);

    [LibraryImport("pdh.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint PdhCollectQueryData(nint query);

    [LibraryImport("pdh.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint PdhGetFormattedCounterArrayW(nint counter, uint format, ref uint bufferSize, out uint itemCount, nint items);

    [LibraryImport("pdh.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint PdhCloseQuery(nint query);

    /// <summary>PDH_FMT_COUNTERVALUE_ITEM_W with a LONGLONG value (24 bytes on x64: name, status, padding, value).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct CounterItem
    {
        public nint Name;
        public uint Status;
        public long Value;
    }
}
