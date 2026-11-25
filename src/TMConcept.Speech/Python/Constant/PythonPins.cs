namespace TMConcept.Speech.Python;

/// <summary>
/// The pins of the Python voices besides uv itself (<c>ToolManifest.UvVersion</c>): the Python that uv installs (a python-build-standalone
/// CPython, the same version for every environment; the voices' packages need 3.10 to 3.12), the date the lock files were made against
/// (<c>uv pip compile --exclude-newer</c>; given again at install, so nothing newer can slip in) and the PyTorch builds.
/// </summary>
public static class PythonPins
{
    /// <summary>The Python version (uv: <c>cpython-3.11.18</c>).</summary>
    public const string PythonVersion = "3.11.18";

    /// <summary>
    /// The moment a lock file is made against when its header names none (every lock names its own since 0.1.54; see
    /// <see cref="PythonFiles.ExcludeNewer"/>).
    /// </summary>
    public const string ExcludeNewer = "2026-11-01T00:00:00Z";

    /// <summary>PyTorch with CUDA 12.8 (NVIDIA cards of compute capability 7.0 or higher, GTX 16 / RTX 20 to RTX 50: PyTorch 2.8 dropped Maxwell and Pascal from this build; needs an NVIDIA driver of 570 or newer; see SpeechDevices).</summary>
    public const string CudaBackend = "cu128";

    /// <summary>PyTorch for the processor (AMD, Intel or no graphics card).</summary>
    public const string CpuBackend = "cpu";

    /// <summary>The only package index used besides PyTorch's own (a user's pip or uv settings are ignored).</summary>
    public const string PackageIndex = "https://pypi.org/simple";

    /// <summary>About how much uv downloads for the Python itself.</summary>
    public const long PythonBytes = 30_000_000;
}
