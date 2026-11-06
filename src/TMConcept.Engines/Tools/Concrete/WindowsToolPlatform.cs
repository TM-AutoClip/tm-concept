using System.IO.Compression;
using TMConcept.Core.Processes;
using TMConcept.Core.Tools;

namespace TMConcept.Engines.Tools;

/// <summary>Windows: zip archives with .NET, tar archives (voice models) with the tar.exe that ships with Windows, ".exe", no system programs.</summary>
public sealed class WindowsToolPlatform(IProcessRunner runner) : IToolPlatform
{
    /// <inheritdoc />
    public string Rid => "win-x64";

    /// <inheritdoc />
    public bool UseSystemPrograms => false;

    /// <inheritdoc />
    public string Executable(string name) => name + ".exe";

    /// <inheritdoc />
    public async Task ExtractAsync(string archive, string targetFolder, CancellationToken cancellationToken)
    {
        if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            await Task.Run(() => ZipFile.ExtractToDirectory(archive, targetFolder, overwriteFiles: false), cancellationToken).ConfigureAwait(false);
            return;
        }

        var tar = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tar.exe");
        var result = await runner.RunAsync(new ProcessStart(tar, ["-xf", archive, "-C", targetFolder], targetFolder), null, cancellationToken)
            .ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException("tar failed: " + Core.Processes.ChildOutput.Summary(result.LastLines));
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> MissingLibrariesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);
}
