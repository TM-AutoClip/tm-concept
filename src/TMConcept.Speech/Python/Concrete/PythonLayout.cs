using TMConcept.Core.Paths;

namespace TMConcept.Speech.Python;

/// <summary>
/// Where the Python voices live: everything under <c>&lt;tools&gt;/speech-py</c>, so "Reinstall components" and Remove take it away and nothing
/// lands in the user's profile (no registry entry, no python on the PATH, no uv or pip settings read). <c>python/</c> the Python uv installs,
/// <c>cache/</c> uv's download cache (shared by the environments: PyTorch is downloaded once per build), <c>env-&lt;key&gt;/</c> one virtual
/// environment per <see cref="PythonEnvironment"/>, <c>models/&lt;code&gt;/</c> the model files of a voice, <c>scripts/</c> the worker and
/// its adapters, <c>home/</c> the folders libraries write to (Hugging Face, PyTorch, matplotlib, numba).
/// </summary>
public sealed class PythonLayout(IAppPaths paths)
{
    /// <summary>The marker file of an installed environment (holds its fingerprint).</summary>
    public const string Marker = ".tmc-installed";

    /// <summary>The root.</summary>
    public string Root => Path.Combine(paths.ToolsDirectory, "speech-py");

    /// <summary>The Python uv installs (<c>UV_PYTHON_INSTALL_DIR</c>).</summary>
    public string PythonFolder => Path.Combine(Root, "python");

    /// <summary>The file that names the installed interpreter (written after <c>uv python find</c>).</summary>
    public string PythonMarker => Path.Combine(PythonFolder, ".tmc-python-" + PythonPins.PythonVersion);

    /// <summary>uv's cache.</summary>
    public string CacheFolder => Path.Combine(Root, "cache");

    /// <summary>The scripts the worker runs.</summary>
    public string ScriptsFolder => Path.Combine(Root, "scripts");

    /// <summary>The lock files given to uv.</summary>
    public string LocksFolder => Path.Combine(Root, "locks");

    /// <summary>The folder libraries write their settings and caches to (instead of the user's home).</summary>
    public string HomeFolder => Path.Combine(Root, "home");

    /// <summary>The folder of an environment.</summary>
    public string EnvironmentFolder(string key) => Path.Combine(Root, "env-" + key);

    /// <summary>The interpreter of an environment.</summary>
    public string EnvironmentPython(string key) => OperatingSystem.IsWindows()
        ? Path.Combine(EnvironmentFolder(key), "Scripts", "python.exe")
        : Path.Combine(EnvironmentFolder(key), "bin", "python");

    /// <summary>The marker of an environment.</summary>
    public string EnvironmentMarker(string key) => Path.Combine(EnvironmentFolder(key), Marker);

    /// <summary>The model folder of a voice.</summary>
    public string ModelFolder(string code) => Path.Combine(Root, "models", code);
}
