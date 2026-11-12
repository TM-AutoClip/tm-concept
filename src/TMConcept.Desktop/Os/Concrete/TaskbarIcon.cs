using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;

namespace TMConcept.Desktop.Os;

/// <summary>
/// Windows: without an AppUserModelID of its own the taskbar resolves the window from the exe path and (Windows 11, Nov 2026, 0.1.12 and
/// 0.1.13) showed Windows' generic "application" icon, although the window answered WM_GETICON with the app icon and the exe carries it
/// too. Neither a process-wide id set at start nor WM_SETICON through DefWindowProc changed the button; giving the open window its own id
/// (with the relaunch command, display name and icon a pinned button needs) makes the taskbar remake the button with the app icon.
/// The property store is called through its vtable: the published app is trimmed, which turns built-in COM interop off.
/// </summary>
public static unsafe partial class TaskbarIcon
{
    /// <summary>The id: company.product, the same for every version so a pinned button keeps working after an update.</summary>
    public const string AppId = "TMAutoClip.TMConcept";

    private const uint RelaunchCommand = 2;
    private const uint RelaunchIconResource = 3;
    private const uint RelaunchDisplayName = 4;
    private const uint Id = 5;
    private const ushort LongString = 31;
    private static readonly Guid PropertyStore = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
    private static readonly Guid AppUserModel = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");

    /// <summary>Once the window is open and the UI thread is free, gives it the app's taskbar identity (logged).</summary>
    public static void ApplyWhenOpen(Window window, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(logger);
        window.Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            var result = Apply(window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);
            if (result == 0)
            {
                logger.LogInformation("Taskbar identity {AppId} given to {Window}", AppId, window.GetType().Name);
            }
            else
            {
                logger.LogWarning("Taskbar identity could not be given to {Window} (Windows result {Result:X8}); the taskbar may show a generic icon",
                    window.GetType().Name, result);
            }
        }, DispatcherPriority.ApplicationIdle);
    }

    /// <summary>Gives the window the id and its relaunch properties. 0 = done (also elsewhere than Windows, where there is nothing to do).</summary>
    public static int Apply(IntPtr window)
    {
        if (!OperatingSystem.IsWindows() || window == IntPtr.Zero)
        {
            return 0;
        }

        var hr = SHGetPropertyStoreForWindow(window, in PropertyStore, out var store);
        if (hr != 0)
        {
            return hr;
        }

        try
        {
            var exe = Environment.ProcessPath ?? string.Empty;
            if (exe.Length > 0)
            {
                // The relaunch properties go together and before the id: a pinned button then starts this exe with this name and icon.
                hr = Set(store, RelaunchCommand, $"\"{exe}\"");
                hr = hr != 0 ? hr : Set(store, RelaunchDisplayName, "TM Concept");
                hr = hr != 0 ? hr : Set(store, RelaunchIconResource, $"{exe},0");
            }

            hr = hr != 0 ? hr : Set(store, Id, AppId);
            return hr != 0 ? hr : Call(store, 7);
        }
        finally
        {
            Call(store, 2);
        }
    }

    /// <summary>IPropertyStore::SetValue with a VT_LPWSTR value (the store copies it).</summary>
    private static int Set(IntPtr store, uint id, string value)
    {
        var key = stackalloc byte[20];
        var variant = stackalloc byte[24];
        new Span<byte>(variant, 24).Clear();
        var fmtid = AppUserModel;
        new ReadOnlySpan<byte>(&fmtid, 16).CopyTo(new Span<byte>(key, 16));
        *(uint*)(key + 16) = id;
        *(ushort*)variant = LongString;
        fixed (char* text = value)
        {
            *(char**)(variant + 8) = text;
            var setValue = (delegate* unmanaged[Stdcall]<IntPtr, byte*, byte*, int>)(*(IntPtr**)store)[6];
            return setValue(store, key, variant);
        }
    }

    /// <summary>A method of the store without arguments (2 = Release, 7 = Commit).</summary>
    private static int Call(IntPtr store, int slot)
    {
        var method = (delegate* unmanaged[Stdcall]<IntPtr, int>)(*(IntPtr**)store)[slot];
        return method(store);
    }

    [LibraryImport("shell32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int SHGetPropertyStoreForWindow(IntPtr window, in Guid riid, out IntPtr store);
}
