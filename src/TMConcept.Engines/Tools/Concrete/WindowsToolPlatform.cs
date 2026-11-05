using System.IO.Compression;
using TMConcept.Core.Tools;

namespace TMConcept.Engines.Tools;

/// <summary>Windows: zip archives, ".exe", no system programs, no extra libraries.</summary>
public sealed class WindowsToolPlatform : IToolPlatform
{
    /// <inheritdoc />
    public string Rid => "win-x64";

    /// <inheritdoc />
    public bool UseSystemPrograms => false;

    /// <inheritdoc />
    public string Executable(string name) => name + ".exe";

    /// <inheritdoc />
    public Task ExtractAsync(string archive, string targetFolder, CancellationToken cancellationToken) =>
        Task.Run(() => ZipFile.ExtractToDirectory(archive, targetFolder, overwriteFiles: false), cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> MissingLibrariesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);
}
