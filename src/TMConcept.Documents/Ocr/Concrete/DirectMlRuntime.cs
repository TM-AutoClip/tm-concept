using System.Runtime.InteropServices;

namespace TMConcept.Documents.Ocr;

/// <summary>
/// The DirectML build of ONNX Runtime (0.1.27, Windows), used only through its C function table: the app's managed ONNX Runtime package is
/// bound to its own bundled processor build (1.31.1), and this second copy (1.24.4, the last DirectML build) is loaded by its full path from
/// the tools folder. <c>DirectML.dll</c> is loaded first, by its full path, because Windows has an older copy in System32 that the runtime's
/// delayed import would otherwise find. The positions in the table are those of <c>onnxruntime_c_api.h</c> of 1.24.4 (API version 24; the
/// table only grows). Loaded once per process and never unloaded; sessions are opened and closed freely (closing them frees the card's memory).
/// A session gets the DirectML backend on the fastest graphics card (high performance, graphics cards only), memory patterns off and
/// sequential execution, as the DirectML backend requires. Any failure is an <see cref="InvalidOperationException"/> with ONNX Runtime's reason.
/// </summary>
internal sealed unsafe class DirectMlRuntime
{
    private const uint ApiVersion = 24;
    private const int TensorFloat = 1;

    private static readonly Lock LoadGate = new();
    private static DirectMlRuntime? _loaded;

    private readonly IntPtr* _api;
    private readonly IntPtr _library;
    private readonly IntPtr _env;

    private DirectMlRuntime(string folder)
    {
        // The bundled processor build is bound first, so nothing that looks for "onnxruntime" by name finds this copy.
        _ = Microsoft.ML.OnnxRuntime.OrtEnv.Instance();
        NativeLibrary.Load(Path.Combine(folder, DirectMlRuntimeRelease.DirectMlFile));
        _library = NativeLibrary.Load(Path.Combine(folder, DirectMlRuntimeRelease.RuntimeFile));
        var getBase = (delegate* unmanaged<IntPtr*>)NativeLibrary.GetExport(_library, "OrtGetApiBase");
        var apiBase = getBase();
        var getApi = (delegate* unmanaged<uint, IntPtr*>)apiBase[0];
        var getVersion = (delegate* unmanaged<IntPtr>)apiBase[1];
        Version = Marshal.PtrToStringUTF8(getVersion()) ?? "?";
        _api = getApi(ApiVersion);
        if (_api == null)
        {
            throw new InvalidOperationException("The ONNX Runtime library (" + Version + ") does not offer API version " + ApiVersion + ".");
        }

        var name = Marshal.StringToCoTaskMemUTF8("tm-concept-ocr");
        try
        {
            IntPtr env;

            // Logging level 3 = errors only.
            Check(((delegate* unmanaged<int, IntPtr, IntPtr*, IntPtr>)_api[3])(3, name, &env));
            _env = env;
        }
        finally
        {
            Marshal.FreeCoTaskMem(name);
        }
    }

    /// <summary>The library's version ("1.24.4").</summary>
    public string Version { get; }

    /// <summary>
    /// The runtime of <paramref name="folder"/>, loaded once (<see cref="InvalidOperationException"/> when it cannot be: a missing or broken
    /// file, no DirectX 12).
    /// </summary>
    public static DirectMlRuntime Load(string folder)
    {
        lock (LoadGate)
        {
            if (_loaded is null)
            {
                try
                {
                    _loaded = new DirectMlRuntime(folder);
                }
                catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
                {
                    throw new InvalidOperationException("The DirectML runtime could not be loaded: " + ex.Message, ex);
                }
            }

            return _loaded;
        }
    }

    /// <summary>Opens <paramref name="file"/> on the graphics card; its handle and its first input's and first output's names.</summary>
    public (IntPtr Session, string Input, string Output) Open(string file)
    {
        IntPtr options;
        Check(((delegate* unmanaged<IntPtr*, IntPtr>)_api[10])(&options));
        try
        {
            // ORT_SEQUENTIAL, memory patterns off, every graph optimisation (ORT_ENABLE_ALL = 99), one thread for what stays on the processor.
            Check(((delegate* unmanaged<IntPtr, int, IntPtr>)_api[13])(options, 0));
            Check(((delegate* unmanaged<IntPtr, IntPtr>)_api[17])(options));
            Check(((delegate* unmanaged<IntPtr, int, IntPtr>)_api[23])(options, 99));
            Check(((delegate* unmanaged<IntPtr, int, IntPtr>)_api[24])(options, 1));
            AppendDirectMl(options);
            var path = Marshal.StringToHGlobalUni(file);
            try
            {
                IntPtr session;
                Check(((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr*, IntPtr>)_api[7])(_env, path, options, &session));
                try
                {
                    return (session, Name(session, 30, 36), Name(session, 31, 37));
                }
                catch
                {
                    Close(session);
                    throw;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(path);
            }
        }
        finally
        {
            ((delegate* unmanaged<IntPtr, void>)_api[100])(options);
        }
    }

    /// <summary>Runs <paramref name="session"/> with one float input; its first output with its shape.</summary>
    public (float[] Data, int[] Shape) Run(IntPtr session, string input, string output, float[] data, int[] shape)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(shape);
        IntPtr memory;
        Check(((delegate* unmanaged<int, int, IntPtr*, IntPtr>)_api[69])(1, 0, &memory));
        var dims = shape.Select(s => (long)s).ToArray();
        var inputName = Marshal.StringToCoTaskMemUTF8(input);
        var outputName = Marshal.StringToCoTaskMemUTF8(output);
        IntPtr value = IntPtr.Zero;
        IntPtr result = IntPtr.Zero;
        try
        {
            fixed (float* pointer = data)
            fixed (long* d = dims)
            {
                Check(((delegate* unmanaged<IntPtr, IntPtr, nuint, long*, nuint, int, IntPtr*, IntPtr>)_api[49])(memory, (IntPtr)pointer,
                    (nuint)(data.Length * sizeof(float)), d, (nuint)dims.Length, TensorFloat, &value));
                Check(((delegate* unmanaged<IntPtr, IntPtr, IntPtr*, IntPtr*, nuint, IntPtr*, nuint, IntPtr*, IntPtr>)_api[9])(session, IntPtr.Zero,
                    &inputName, &value, 1, &outputName, 1, &result));
            }

            IntPtr info;
            Check(((delegate* unmanaged<IntPtr, IntPtr*, IntPtr>)_api[65])(result, &info));
            int[] outShape;
            try
            {
                nuint count;
                Check(((delegate* unmanaged<IntPtr, nuint*, IntPtr>)_api[61])(info, &count));
                var outDims = new long[(int)count];
                fixed (long* o = outDims)
                {
                    Check(((delegate* unmanaged<IntPtr, long*, nuint, IntPtr>)_api[62])(info, o, count));
                }

                outShape = outDims.Select(v => checked((int)v)).ToArray();
            }
            finally
            {
                ((delegate* unmanaged<IntPtr, void>)_api[99])(info);
            }

            IntPtr values;
            Check(((delegate* unmanaged<IntPtr, IntPtr*, IntPtr>)_api[51])(result, &values));
            var output32 = new float[outShape.Aggregate(1L, (a, b) => a * b)];
            Marshal.Copy(values, output32, 0, output32.Length);
            return (output32, outShape);
        }
        finally
        {
            if (result != IntPtr.Zero)
            {
                ((delegate* unmanaged<IntPtr, void>)_api[96])(result);
            }

            if (value != IntPtr.Zero)
            {
                ((delegate* unmanaged<IntPtr, void>)_api[96])(value);
            }

            Marshal.FreeCoTaskMem(inputName);
            Marshal.FreeCoTaskMem(outputName);
            ((delegate* unmanaged<IntPtr, void>)_api[94])(memory);
        }
    }

    /// <summary>Closes a session (its graphics memory is freed).</summary>
    public void Close(IntPtr session) => ((delegate* unmanaged<IntPtr, void>)_api[95])(session);

    /// <summary>
    /// The DirectML backend on the options: through the DirectML API of the runtime (<c>SessionOptionsAppendExecutionProvider_DML2</c>, the
    /// high-performance graphics card, so a laptop's own card is chosen over the built-in one), or the exported call on card 0 when that API is
    /// not offered.
    /// </summary>
    private void AppendDirectMl(IntPtr options)
    {
        var provider = Marshal.StringToCoTaskMemUTF8("DML");
        try
        {
            IntPtr* dml = null;
            var status = ((delegate* unmanaged<IntPtr, uint, IntPtr**, IntPtr>)_api[195])(provider, ApiVersion, &dml);
            if (status == IntPtr.Zero && dml != null)
            {
                // OrtDmlDeviceOptions { Preference = HighPerformance (1), Filter = Gpu (1) }.
                var device = stackalloc uint[2];
                device[0] = 1;
                device[1] = 1;
                Check(((delegate* unmanaged<IntPtr, uint*, IntPtr>)dml[5])(options, device));
                return;
            }

            if (status != IntPtr.Zero)
            {
                ((delegate* unmanaged<IntPtr, void>)_api[93])(status);
            }

            var append = (delegate* unmanaged<IntPtr, int, IntPtr>)NativeLibrary.GetExport(_library, "OrtSessionOptionsAppendExecutionProvider_DML");
            Check(append(options, 0));
        }
        finally
        {
            Marshal.FreeCoTaskMem(provider);
        }
    }

    private string Name(IntPtr session, int countIndex, int nameIndex)
    {
        IntPtr allocator;
        Check(((delegate* unmanaged<IntPtr*, IntPtr>)_api[78])(&allocator));
        nuint count;
        Check(((delegate* unmanaged<IntPtr, nuint*, IntPtr>)_api[countIndex])(session, &count));
        if (count == 0)
        {
            throw new InvalidOperationException("ONNX Runtime: the OCR model has no input or output.");
        }

        IntPtr name;
        Check(((delegate* unmanaged<IntPtr, nuint, IntPtr, IntPtr*, IntPtr>)_api[nameIndex])(session, 0, allocator, &name));
        try
        {
            return Marshal.PtrToStringUTF8(name) ?? string.Empty;
        }
        finally
        {
            Check(((delegate* unmanaged<IntPtr, IntPtr, IntPtr>)_api[76])(allocator, name));
        }
    }

    private void Check(IntPtr status)
    {
        if (status == IntPtr.Zero)
        {
            return;
        }

        var message = Marshal.PtrToStringUTF8(((delegate* unmanaged<IntPtr, IntPtr>)_api[2])(status)) ?? "unknown error";
        ((delegate* unmanaged<IntPtr, void>)_api[93])(status);
        throw new InvalidOperationException("ONNX Runtime (DirectML): " + message);
    }
}
