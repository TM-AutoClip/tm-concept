namespace TMConcept.Core.Tools;

/// <summary>Operating system facts for tools (from DI; no "if Windows" in the code).</summary>
public interface IToolPlatform
{
    /// <summary>Runtime id of the downloads ("win-x64", "linux-x64", "linux-arm64").</summary>
    string Rid { get; }

    /// <summary>Executable name ("node.exe" / "node").</summary>
    string Executable(string name);

    /// <summary>Whether a program found on the system PATH may be used when the app has no copy (Linux: the distribution's ffmpeg).</summary>
    bool UseSystemPrograms { get; }

    /// <summary>Unpacks an archive into an empty folder.</summary>
    Task ExtractAsync(string archive, string targetFolder, CancellationToken cancellationToken);

    /// <summary>System libraries Chrome needs but that are missing (empty on Windows).</summary>
    Task<IReadOnlyList<string>> MissingLibrariesAsync(CancellationToken cancellationToken);
}
