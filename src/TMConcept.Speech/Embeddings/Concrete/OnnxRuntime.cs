using System.Runtime.InteropServices;

namespace TMConcept.Speech.Embeddings;

/// <summary>
/// The few ONNX Runtime C API calls the local embedding model needs, through the function table of the ONNX Runtime library that comes with
/// the sherpa-onnx libraries (already downloaded for the local voices; no package, no second copy). The table is append-only in every ONNX
/// Runtime version, so the positions below (taken from onnxruntime_c_api.h, API version 11 and later) do not move. Used only in the worker
/// process, never in the window process.
/// </summary>
internal sealed unsafe class OnnxRuntime : IDisposable
{
    private const uint ApiVersion = 11;
    private const int TensorFloat = 1;
    private const int TensorInt64 = 7;

    private readonly IntPtr* _api;
    private readonly IntPtr _library;
    private IntPtr _env;

    /// <summary>Loads <c>onnxruntime</c> from <paramref name="libraryFolder"/> and creates its environment.</summary>
    public OnnxRuntime(string libraryFolder)
    {
        var file = Path.Combine(libraryFolder, OperatingSystem.IsWindows() ? "onnxruntime.dll" : "libonnxruntime.so");
        _library = NativeLibrary.Load(file);
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

        var name = Marshal.StringToCoTaskMemUTF8("tm-concept");
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

    /// <summary>The library's version ("1.17.1").</summary>
    public string Version { get; }

    /// <summary>Opens a model file of the current folder with <paramref name="threads"/> threads.</summary>
    public Session Open(string file, int threads)
    {
        IntPtr options;
        Check(((delegate* unmanaged<IntPtr*, IntPtr>)_api[10])(&options));
        try
        {
            Check(((delegate* unmanaged<IntPtr, int, IntPtr>)_api[24])(options, threads));
            Check(((delegate* unmanaged<IntPtr, int, IntPtr>)_api[25])(options, 1));
            var path = OperatingSystem.IsWindows() ? Marshal.StringToHGlobalUni(file) : Marshal.StringToHGlobalAnsi(file);
            try
            {
                IntPtr session;
                Check(((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr*, IntPtr>)_api[7])(_env, path, options, &session));
                return new Session(this, session, Names(session, 30, 36), Names(session, 31, 37));
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

    /// <summary>
    /// Runs the session with int64 inputs of shape [1, n] (by name; an input the model does not have is skipped) and returns the first
    /// output as floats with its shape.
    /// </summary>
    public (float[] Values, long[] Shape) Run(Session session, IReadOnlyDictionary<string, long[]> inputs)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(inputs);
        IntPtr memory;
        Check(((delegate* unmanaged<int, int, IntPtr*, IntPtr>)_api[69])(1, 0, &memory));
        var names = new List<IntPtr>();
        var values = new List<IntPtr>();
        var pins = new List<GCHandle>();
        IntPtr output = IntPtr.Zero;
        var outputName = Marshal.StringToCoTaskMemUTF8(session.Outputs[0]);
        var shape = stackalloc long[2];
        try
        {
            foreach (var input in session.Inputs)
            {
                if (!inputs.TryGetValue(input, out var data))
                {
                    throw new InvalidOperationException("The embedding model wants an input the app does not give: " + input);
                }

                var pin = GCHandle.Alloc(data, GCHandleType.Pinned);
                pins.Add(pin);
                shape[0] = 1;
                shape[1] = data.Length;
                IntPtr value;
                Check(((delegate* unmanaged<IntPtr, IntPtr, nuint, long*, nuint, int, IntPtr*, IntPtr>)_api[49])(memory, pin.AddrOfPinnedObject(),
                    (nuint)(data.Length * sizeof(long)), shape, 2, TensorInt64, &value));
                values.Add(value);
                names.Add(Marshal.StringToCoTaskMemUTF8(input));
            }

            var nameArray = names.ToArray();
            var valueArray = values.ToArray();
            fixed (IntPtr* inputNames = nameArray)
            fixed (IntPtr* inputValues = valueArray)
            {
                Check(((delegate* unmanaged<IntPtr, IntPtr, IntPtr*, IntPtr*, nuint, IntPtr*, nuint, IntPtr*, IntPtr>)_api[9])(session.Handle, IntPtr.Zero,
                    inputNames, inputValues, (nuint)nameArray.Length, &outputName, 1, &output));
            }

            IntPtr info;
            Check(((delegate* unmanaged<IntPtr, IntPtr*, IntPtr>)_api[65])(output, &info));
            long[] dims;
            try
            {
                nuint count;
                Check(((delegate* unmanaged<IntPtr, nuint*, IntPtr>)_api[61])(info, &count));
                dims = new long[(int)count];
                fixed (long* d = dims)
                {
                    Check(((delegate* unmanaged<IntPtr, long*, nuint, IntPtr>)_api[62])(info, d, count));
                }
            }
            finally
            {
                ((delegate* unmanaged<IntPtr, void>)_api[99])(info);
            }

            IntPtr pointer;
            Check(((delegate* unmanaged<IntPtr, IntPtr*, IntPtr>)_api[51])(output, &pointer));
            var length = dims.Aggregate(1L, (a, b) => a * b);
            var result = new float[length];
            Marshal.Copy(pointer, result, 0, result.Length);
            return (result, dims);
        }
        finally
        {
            if (output != IntPtr.Zero)
            {
                ((delegate* unmanaged<IntPtr, void>)_api[96])(output);
            }

            values.ForEach(v => ((delegate* unmanaged<IntPtr, void>)_api[96])(v));
            names.ForEach(Marshal.FreeCoTaskMem);
            pins.ForEach(p => p.Free());
            Marshal.FreeCoTaskMem(outputName);
            ((delegate* unmanaged<IntPtr, void>)_api[94])(memory);
        }
    }

    /// <summary>Closes a session.</summary>
    public void Close(IntPtr session) => ((delegate* unmanaged<IntPtr, void>)_api[95])(session);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_env != IntPtr.Zero)
        {
            ((delegate* unmanaged<IntPtr, void>)_api[92])(_env);
            _env = IntPtr.Zero;
        }
    }

    private List<string> Names(IntPtr session, int countIndex, int nameIndex)
    {
        IntPtr allocator;
        Check(((delegate* unmanaged<IntPtr*, IntPtr>)_api[78])(&allocator));
        nuint count;
        Check(((delegate* unmanaged<IntPtr, nuint*, IntPtr>)_api[countIndex])(session, &count));
        var names = new List<string>();
        for (nuint i = 0; i < count; i++)
        {
            IntPtr name;
            Check(((delegate* unmanaged<IntPtr, nuint, IntPtr, IntPtr*, IntPtr>)_api[nameIndex])(session, i, allocator, &name));
            names.Add(Marshal.PtrToStringUTF8(name) ?? string.Empty);
            Check(((delegate* unmanaged<IntPtr, IntPtr, IntPtr>)_api[76])(allocator, name));
        }

        return names;
    }

    private void Check(IntPtr status)
    {
        if (status == IntPtr.Zero)
        {
            return;
        }

        var message = Marshal.PtrToStringUTF8(((delegate* unmanaged<IntPtr, IntPtr>)_api[2])(status)) ?? "unknown error";
        ((delegate* unmanaged<IntPtr, void>)_api[93])(status);
        throw new InvalidOperationException("ONNX Runtime: " + message);
    }

    /// <summary>An open model: its handle and the names of its inputs and outputs.</summary>
    internal sealed class Session(OnnxRuntime runtime, IntPtr handle, IReadOnlyList<string> inputs, IReadOnlyList<string> outputs) : IDisposable
    {
        /// <summary>The native handle.</summary>
        public IntPtr Handle { get; private set; } = handle;

        /// <summary>Input names.</summary>
        public IReadOnlyList<string> Inputs { get; } = inputs;

        /// <summary>Output names.</summary>
        public IReadOnlyList<string> Outputs { get; } = outputs;

        /// <inheritdoc />
        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
            {
                runtime.Close(Handle);
                Handle = IntPtr.Zero;
            }
        }
    }
}
