using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Hardware;

namespace TMConcept.Engines.Hardware;

/// <summary>
/// Windows: the graphics cards from DXGI (<c>CreateDXGIFactory1</c> → <c>IDXGIFactory1::EnumAdapters1</c> → <c>IDXGIAdapter1::GetDesc1</c>),
/// with each card's own memory as a 64-bit value (WMI's is 32-bit and stops at 4 GB). Needs no rights, no window and no device. The COM
/// methods are called through their vtable slots with function pointers: built-in COM interop is not in a trimmed app. Software adapters
/// (the flag, and the "Microsoft Basic Render Driver" 0x1414:0x8C) are left out. An integrated card reports only its small reserved memory.
/// </summary>
public sealed unsafe partial class DxgiAdapterSource(ILogger<DxgiAdapterSource> logger) : IGraphicsAdapterSource
{
    private const int NotFound = unchecked((int)0x887A0002);
    private const uint SoftwareFlag = 2;

    // IUnknown 0–2, IDXGIObject 3–6, IDXGIFactory 7–11, IDXGIFactory1: 12 EnumAdapters1. IDXGIAdapter 7–9, IDXGIAdapter1: 10 GetDesc1.
    private const int EnumAdapters1Slot = 12;
    private const int GetDesc1Slot = 10;
    private const int ReleaseSlot = 2;

    /// <inheritdoc />
    public string Name => "DXGI";

    /// <inheritdoc />
    public IReadOnlyList<GpuAdapter> Read()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        try
        {
            return Enumerate();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or SEHException)
        {
            logger.LogWarning(ex, "DXGI could not list the graphics cards");
            return [];
        }
    }

    private static List<GpuAdapter> Enumerate()
    {
        var list = new List<GpuAdapter>();
        var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
        nint factory = 0;
        if (CreateDXGIFactory1(&iid, &factory) < 0 || factory == 0)
        {
            return list;
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
                    if (software)
                    {
                        continue;
                    }

                    var name = new string(description.Description).TrimEnd('\0').Trim();
                    var memory = (long)description.DedicatedVideoMemory;
                    list.Add(new GpuAdapter(PciVendors.Vendor(description.VendorId), name, memory > 0 ? memory : null));
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

        return list;
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
}
