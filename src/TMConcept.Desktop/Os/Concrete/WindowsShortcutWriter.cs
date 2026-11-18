using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace TMConcept.Desktop.Os;

/// <summary>
/// Windows: writes <c>TM Concept.lnk</c> on the desktop and in the Start menu (the places Setup uses) through the shell's ShellLink object,
/// with the taskbar id so a pinned button and the window group together. Called through the vtables, like <see cref="TaskbarIcon"/>: the
/// published app is trimmed, which turns built-in COM interop off (Velopack's own shortcut helper uses it and is obsolete). The shortcuts
/// point into the install folder, so an uninstall still removes them.
/// </summary>
public sealed unsafe partial class WindowsShortcutWriter(ILogger<WindowsShortcutWriter> logger) : IShortcutWriter
{
    /// <summary>The shortcut's file name (Setup's <c>--packTitle</c>).</summary>
    public const string FileName = "TM Concept.lnk";

    private const uint InprocServer = 1;
    private const uint AppUserModelIdProperty = 5;
    private const ushort LongString = 31;
    private static readonly Guid ShellLinkClass = new("00021401-0000-0000-C000-000000000046");
    private static readonly Guid ShellLinkInterface = new("000214F9-0000-0000-C000-000000000046");
    private static readonly Guid PersistFileInterface = new("0000010b-0000-0000-C000-000000000046");
    private static readonly Guid PropertyStoreInterface = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
    private static readonly Guid AppUserModel = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");

    /// <inheritdoc />
    public bool Supported => OperatingSystem.IsWindows();

    /// <inheritdoc />
    public IReadOnlyList<string> Write(string program, string appUserModelId)
    {
        ArgumentException.ThrowIfNullOrEmpty(program);
        ArgumentException.ThrowIfNullOrEmpty(appUserModelId);
        if (!Supported)
        {
            throw new PlatformNotSupportedException("Shortcuts are written on Windows only.");
        }

        var written = new List<string>();
        foreach (var folder in new[] { Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.Programs })
        {
            var path = Path.Combine(Environment.GetFolderPath(folder), FileName);
            var hr = WriteOne(path, program, appUserModelId);
            if (hr != 0)
            {
                throw new IOException($"The shortcut {path} could not be written (Windows result {hr:X8}).", hr);
            }

            logger.LogInformation("Shortcut written: {Path} → {Program}", path, program);
            written.Add(path);
        }

        return written;
    }

    private static int WriteOne(string path, string program, string appUserModelId)
    {
        var hr = CoCreateInstance(in ShellLinkClass, IntPtr.Zero, InprocServer, in ShellLinkInterface, out var link);
        if (hr != 0)
        {
            return hr;
        }

        try
        {
            // IShellLinkW: SetWorkingDirectory 9, SetIconLocation 17, SetPath 20.
            hr = CallString(link, 20, program);
            hr = hr != 0 ? hr : CallString(link, 9, Path.GetDirectoryName(program) ?? string.Empty);
            hr = hr != 0 ? hr : SetIcon(link, program);
            hr = hr != 0 ? hr : SetId(link, appUserModelId);
            return hr != 0 ? hr : Save(link, path);
        }
        finally
        {
            Release(link);
        }
    }

    private static int SetIcon(IntPtr link, string program)
    {
        fixed (char* text = program)
        {
            var method = (delegate* unmanaged[Stdcall]<IntPtr, char*, int, int>)(*(IntPtr**)link)[17];
            return method(link, text, 0);
        }
    }

    /// <summary>The taskbar id through the link's IPropertyStore (SetValue 6, Commit 7).</summary>
    private static int SetId(IntPtr link, string appUserModelId)
    {
        var hr = Query(link, PropertyStoreInterface, out var store);
        if (hr != 0)
        {
            return hr;
        }

        try
        {
            var key = stackalloc byte[20];
            var variant = stackalloc byte[24];
            new Span<byte>(variant, 24).Clear();
            var fmtid = AppUserModel;
            new ReadOnlySpan<byte>(&fmtid, 16).CopyTo(new Span<byte>(key, 16));
            *(uint*)(key + 16) = AppUserModelIdProperty;
            *(ushort*)variant = LongString;
            fixed (char* text = appUserModelId)
            {
                *(char**)(variant + 8) = text;
                var setValue = (delegate* unmanaged[Stdcall]<IntPtr, byte*, byte*, int>)(*(IntPtr**)store)[6];
                hr = setValue(store, key, variant);
            }

            if (hr != 0)
            {
                return hr;
            }

            var commit = (delegate* unmanaged[Stdcall]<IntPtr, int>)(*(IntPtr**)store)[7];
            return commit(store);
        }
        finally
        {
            Release(store);
        }
    }

    /// <summary>IPersistFile::Save (slot 6) to <paramref name="path"/>.</summary>
    private static int Save(IntPtr link, string path)
    {
        var hr = Query(link, PersistFileInterface, out var file);
        if (hr != 0)
        {
            return hr;
        }

        try
        {
            fixed (char* text = path)
            {
                var save = (delegate* unmanaged[Stdcall]<IntPtr, char*, int, int>)(*(IntPtr**)file)[6];
                return save(file, text, 1);
            }
        }
        finally
        {
            Release(file);
        }
    }

    private static int CallString(IntPtr target, int slot, string value)
    {
        fixed (char* text = value)
        {
            var method = (delegate* unmanaged[Stdcall]<IntPtr, char*, int>)(*(IntPtr**)target)[slot];
            return method(target, text);
        }
    }

    private static int Query(IntPtr target, Guid interfaceId, out IntPtr result)
    {
        IntPtr found;
        var query = (delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)(*(IntPtr**)target)[0];
        var hr = query(target, &interfaceId, &found);
        result = hr == 0 ? found : IntPtr.Zero;
        return hr;
    }

    private static void Release(IntPtr target)
    {
        var release = (delegate* unmanaged[Stdcall]<IntPtr, uint>)(*(IntPtr**)target)[2];
        _ = release(target);
    }

    [LibraryImport("ole32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int CoCreateInstance(in Guid classId, IntPtr outer, uint context, in Guid interfaceId, out IntPtr result);
}
