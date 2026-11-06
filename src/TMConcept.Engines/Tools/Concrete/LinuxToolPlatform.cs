using System.Runtime.InteropServices;
using TMConcept.Core.Processes;
using TMConcept.Core.Tools;

namespace TMConcept.Engines.Tools;

/// <summary>
/// Linux: tar.xz archives unpacked with the system <c>tar</c>, plain names, the distribution's programs may be used, and Chrome's shared
/// libraries are checked with <c>ldconfig -p</c> (the app never uses root; it shows the apt command instead).
/// </summary>
public sealed class LinuxToolPlatform(IProcessRunner runner) : IToolPlatform
{
    /// <summary>Libraries Chrome Headless Shell needs (Debian package names after the colon).</summary>
    public static readonly IReadOnlyList<(string Library, string Package)> ChromeLibraries =
    [
        ("libnss3.so", "libnss3"), ("libnspr4.so", "libnspr4"), ("libatk-1.0.so", "libatk1.0-0"), ("libatk-bridge-2.0.so", "libatk-bridge2.0-0"),
        ("libcups.so", "libcups2"), ("libdrm.so", "libdrm2"), ("libxkbcommon.so", "libxkbcommon0"), ("libXcomposite.so", "libxcomposite1"),
        ("libXdamage.so", "libxdamage1"), ("libXrandr.so", "libxrandr2"), ("libgbm.so", "libgbm1"), ("libpango-1.0.so", "libpango-1.0-0"),
        ("libasound.so", "libasound2"),
    ];

    /// <inheritdoc />
    public string Rid => RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64";

    /// <inheritdoc />
    public bool UseSystemPrograms => true;

    /// <inheritdoc />
    public string Executable(string name) => name;

    /// <inheritdoc />
    public async Task ExtractAsync(string archive, string targetFolder, CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync(new ProcessStart("tar", ["-xf", archive, "-C", targetFolder], targetFolder), null, cancellationToken)
            .ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException("tar failed: " + Core.Processes.ChildOutput.Summary(result.LastLines));
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> MissingLibrariesAsync(CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        var result = await runner.RunAsync(new ProcessStart("ldconfig", ["-p"], "/"), lines.Add, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return [];
        }

        var text = string.Join('\n', lines);
        return ChromeLibraries.Where(l => !text.Contains(l.Library, StringComparison.Ordinal)).Select(l => l.Package).ToList();
    }
}
