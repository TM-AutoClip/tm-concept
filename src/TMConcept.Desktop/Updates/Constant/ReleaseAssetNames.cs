using System.Text.RegularExpressions;

namespace TMConcept.Desktop.Updates;

/// <summary>
/// The file names of a release (0.1.26 on; <c>tools/package.py</c> and <c>tools/publish.ps1</c> write the same names, and a test holds them to
/// it). The portable files carry no version, so a downloaded copy keeps a true name after its updates. The updaters of 0.1.25 and older take
/// the first file that ends with <c>-win-x64.exe</c> / <c>-linux-x64.tar.gz</c> / <c>-linux-arm64.tar.gz</c>: exactly one file per system may
/// end so, and it is the portable one (Setup ends with <c>-Setup.exe</c>, Velopack's packages with <c>.nupkg</c>), so an old copy updates
/// itself to the new portable program.
/// </summary>
public static class ReleaseAssetNames
{
    /// <summary>The portable Windows program.</summary>
    public const string PortableWindows = "TMConcept-Portable-win-x64.exe";

    /// <summary>The portable Linux archive for x64 computers.</summary>
    public const string PortableLinuxX64 = "tm-concept-portable-linux-x64.tar.gz";

    /// <summary>The portable Linux archive for ARM64 computers (Raspberry Pi 5).</summary>
    public const string PortableLinuxArm64 = "tm-concept-portable-linux-arm64.tar.gz";

    /// <summary>The installer of the installed edition (Velopack's <c>{packId}-{channel}-Setup.exe</c>).</summary>
    public const string SetupWindows = "TMConcept-win-x64-Setup.exe";

    /// <summary>The end of every installer's name.</summary>
    public const string SetupSuffix = "-Setup.exe";

    /// <summary>Velopack's package id (the install folder <c>%LOCALAPPDATA%\TMConcept</c> is named after it).</summary>
    public const string PackageId = "TMConcept";

    /// <summary>
    /// A Windows program file named by the release it came from (<c>TMConcept-0.1.17-win-x64.exe</c>, as 0.1.25 and older were published): the
    /// portable updater gives it <see cref="PortableWindows"/> on its next update.
    /// </summary>
    public static readonly Regex VersionedWindowsProgram = new(@"^TMConcept-\d+\.\d+\.\d+(?:-[0-9A-Za-z.]+)?-win-x64\.exe$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
}
