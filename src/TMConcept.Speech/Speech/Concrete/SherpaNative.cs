using System.Reflection;
using System.Runtime.InteropServices;

namespace TMConcept.Speech.Speech;

/// <summary>
/// The sherpa-onnx C API calls the speech worker needs (layout of the 2.0.1 header, checked against the official C# wrapper of 2.0.1). The
/// libraries are not part of the app: they are the official release files in the tools folder, loaded by full path (ONNX Runtime first,
/// which the C API library needs). Only the speech worker process loads them, never the window process.
/// </summary>
internal static partial class SherpaNative
{
    private const string Library = "sherpa-onnx-c-api";
    private static readonly Lock Gate = new();
    private static string? _folder;
    private static IntPtr _handle;

    /// <summary>The library files that must be in the folder.</summary>
    public static IReadOnlyList<string> Files => [FileName("onnxruntime"), FileName(Library)];

    /// <summary>Points the calls at the library folder (once per process; a second, different folder is an error).</summary>
    public static void Use(string folder)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        lock (Gate)
        {
            if (_folder is not null)
            {
                if (!string.Equals(_folder, folder, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The speech libraries are already loaded from another folder.");
                }

                return;
            }

            _folder = folder;
            NativeLibrary.SetDllImportResolver(typeof(SherpaNative).Assembly, Resolve);
        }
    }

    /// <summary>The file name of a library on this system ("onnxruntime" → onnxruntime.dll / libonnxruntime.so).</summary>
    public static string FileName(string name) => OperatingSystem.IsWindows() ? name + ".dll" : "lib" + name + ".so";

    [LibraryImport(Library)]
    internal static partial IntPtr SherpaOnnxCreateOfflineTts(in SherpaTtsConfig config);

    [LibraryImport(Library)]
    internal static partial void SherpaOnnxDestroyOfflineTts(IntPtr tts);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr SherpaOnnxOfflineTtsGenerateWithConfig(IntPtr tts, string text, in SherpaGenerationConfig config, IntPtr callback,
        IntPtr argument);

    [LibraryImport(Library)]
    internal static partial void SherpaOnnxDestroyOfflineTtsGeneratedAudio(IntPtr audio);

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (name != Library)
        {
            return IntPtr.Zero;
        }

        lock (Gate)
        {
            if (_handle == IntPtr.Zero)
            {
                var folder = _folder ?? throw new InvalidOperationException("The speech library folder is not set.");
                NativeLibrary.Load(Path.Combine(folder, FileName("onnxruntime")));
                _handle = NativeLibrary.Load(Path.Combine(folder, FileName(Library)));
            }

            return _handle;
        }
    }
}
