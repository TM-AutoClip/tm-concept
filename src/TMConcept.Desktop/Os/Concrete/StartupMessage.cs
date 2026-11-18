using System.Runtime.InteropServices;

namespace TMConcept.Desktop.Os;

/// <summary>
/// A message for a start that cannot go on before any window exists (the user would otherwise see nothing): a plain Windows message box, the
/// error output elsewhere. Used only by the running app, never by the tests.
/// </summary>
public static partial class StartupMessage
{
    private const uint ErrorIcon = 0x10;

    /// <summary>Shows <paramref name="text"/> and waits until it is closed.</summary>
    public static void Show(string title, string text)
    {
        Console.Error.WriteLine(title + ": " + text);
        if (OperatingSystem.IsWindows())
        {
            _ = MessageBox(IntPtr.Zero, text, title, ErrorIcon);
        }
    }

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int MessageBox(IntPtr owner, string text, string caption, uint type);
}
