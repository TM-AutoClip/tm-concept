using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Hardware;

namespace TMConcept.Engines.Hardware;

/// <summary>
/// Windows: the graphics cards from DXGI (<c>CreateDXGIFactory1</c> → <c>IDXGIFactory1::EnumAdapters1</c> → <c>IDXGIAdapter1::GetDesc1</c>),
/// with each card's own memory as a 64-bit value (WMI's is 32-bit and stops at 4 GB) and its LUID as the card's <see cref="GpuAdapter.Key"/>.
/// Needs no rights, no window and no device. The COM methods are called through their vtable slots with function pointers: built-in COM
/// interop is not in a trimmed app. Software adapters (the flag, and the "Microsoft Basic Render Driver" 0x1414:0x8C) are left out. An
/// integrated card reports only its small reserved memory. As a memory source it reads this process's budget of a card
/// (<c>IDXGIAdapter3::QueryVideoMemoryInfo</c>, local segment group: budget − current usage), which Windows lowers when other programs use
/// the card; the Vulkan build of sd-cli sees the same budget.
/// </summary>
public sealed unsafe partial class DxgiAdapterSource(ILogger<DxgiAdapterSource> logger) : IGraphicsAdapterSource, IVideoMemorySource
{
    private const int NotFound = unchecked((int)0x887A0002);
    private const uint SoftwareFlag = 2;

    // IUnknown 0–2, IDXGIObject 3–6, IDXGIFactory 7–11, IDXGIFactory1: 12 EnumAdapters1. IDXGIAdapter 7–9, IDXGIAdapter1: 10 GetDesc1,
    // IDXGIAdapter2: 11 GetDesc2, IDXGIAdapter3: 12 RegisterHardwareContentProtectionTeardownStatusEvent, 13 Unregister…, 14 QueryVideoMemoryInfo
    // (checked against dxgi1_4.h of the Windows SDK 10.0.22621).
    private const int QueryInterfaceSlot = 0;
    private const int EnumAdapters1Slot = 12;
    private const int GetDesc1Slot = 10;
    private const int QueryVideoMemoryInfoSlot = 14;
    private const int ReleaseSlot = 2;

    /// <inheritdoc cref="IGraphicsAdapterSource.Name" />
    public string Name => "DXGI";

    /// <inheritdoc />
    string IVideoMemorySource.Name => "DXGI budget";

    /// <inheritdoc />
    public IReadOnlyList<GpuAdapter> Read()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        try
        {
            var list = new List<GpuAdapter>();
            Walk((_, description) =>
            {
                var name = new string(description.Description).TrimEnd('\0').Trim();
                var memory = (long)description.DedicatedVideoMemory;
                list.Add(new GpuAdapter(PciVendors.Vendor(description.VendorId), name, memory > 0 ? memory : null, Luid(description)));
                return true;
            });
            return list;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or SEHException)
        {
            logger.LogWarning(ex, "DXGI could not list the graphics cards");
            return [];
        }
    }

    /// <inheritdoc />
    public Task<long?> FreeBytesAsync(GpuAdapter adapter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        if (!OperatingSystem.IsWindows() || adapter.Key.Length == 0)
        {
            return Task.FromResult<long?>(null);
        }

        try
        {
            long? free = null;
            Walk((pointer, description) =>
            {
                if (!string.Equals(Luid(description), adapter.Key, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                free = Budget(pointer);
                return false;
            });
            return Task.FromResult(free);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or SEHException)
        {
            logger.LogWarning(ex, "DXGI could not read the graphics card's memory budget");
            return Task.FromResult<long?>(null);
        }
    }

    /// <summary>The performance counter's name of a card's LUID: <c>luid_0x{high:x8}_0x{low:x8}</c>.</summary>
    internal static string LuidKey(int high, uint low) => string.Create(CultureInfo.InvariantCulture, $"luid_0x{(uint)high:x8}_0x{low:x8}");

    private static string Luid(AdapterDescription description) => LuidKey(description.LuidHigh, description.LuidLow);

    /// <summary>Budget − current usage of the local (own) memory segment group; null when IDXGIAdapter3 is not there (before Windows 10).</summary>
    private static long? Budget(nint adapter)
    {
        var iid = new Guid("645967a4-1392-4310-a798-8053ce3e93fd");
        nint adapter3 = 0;
        var queryInterface = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)(*(void***)adapter)[QueryInterfaceSlot];
        if (queryInterface(adapter, &iid, &adapter3) < 0 || adapter3 == 0)
        {
            return null;
        }

        try
        {
            var query = (delegate* unmanaged[Stdcall]<nint, uint, int, MemoryInfo*, int>)(*(void***)adapter3)[QueryVideoMemoryInfoSlot];
            MemoryInfo info;
            if (query(adapter3, 0, 0, &info) < 0 || info.Budget == 0)
            {
                return null;
            }

            return info.Budget > info.CurrentUsage ? (long)(info.Budget - info.CurrentUsage) : 0;
        }
        finally
        {
            Release(adapter3);
        }
    }

    /// <summary>Calls <paramref name="visit"/> for every hardware adapter until it returns false.</summary>
    private static void Walk(Func<nint, AdapterDescription, bool> visit)
    {
        var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
        nint factory = 0;
        if (CreateDXGIFactory1(&iid, &factory) < 0 || factory == 0)
        {
            return;
        }

        try
        {
            var enumAdapters1 = (delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)(*(void***)factory)[EnumAdapters1Slot];
            for (uint i = 0; i < 64; i++)
            {
                nint adapter = 0;
                var result = enumAdapters1(factory, i, &adapter);
                if (result == NotFound || result < 0 || adapter == 0)
                {
                    break;
                }

                try
                {
                    var getDesc1 = (delegate* unmanaged[Stdcall]<nint, AdapterDescription*, int>)(*(void***)adapter)[GetDesc1Slot];
                    AdapterDescription description;
                    if (getDesc1(adapter, &description) < 0)
                    {
                        continue;
                    }

                    var software = (description.Flags & SoftwareFlag) != 0 || (description.VendorId == 0x1414 && description.DeviceId == 0x8C);
                    if (!software && !visit(adapter, description))
                    {
                        break;
                    }
                }
                finally
                {
                    Release(adapter);
                }
            }
        }
        finally
        {
            Release(factory);
        }
    }

    private static void Release(nint pointer)
    {
        if (pointer != 0)
        {
            ((delegate* unmanaged[Stdcall]<nint, uint>)(*(void***)pointer)[ReleaseSlot])(pointer);
        }
    }

    // Only System32: the default search also looks in the app's folder, and dxgi.dll is not a KnownDLL, so a dxgi.dll put next to the exe
    // (run from Downloads) would be loaded and run inside the app (the same rule as NativeRuntimeProbe's full System32 paths).
    [LibraryImport("dxgi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int CreateDXGIFactory1(Guid* riid, nint* factory);

    /// <summary>DXGI_ADAPTER_DESC1 (312 bytes on x64; SIZE_T fields are pointer-sized).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct AdapterDescription
    {
        public fixed char Description[128];
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public uint LuidLow;
        public int LuidHigh;
        public uint Flags;
    }

    /// <summary>DXGI_QUERY_VIDEO_MEMORY_INFO (four UINT64).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryInfo
    {
        public ulong Budget;
        public ulong CurrentUsage;
        public ulong AvailableForReservation;
        public ulong CurrentReservation;
    }
}
