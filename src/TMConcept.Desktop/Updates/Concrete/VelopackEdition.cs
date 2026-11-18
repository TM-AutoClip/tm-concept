using Velopack.Locators;

namespace TMConcept.Desktop.Updates;

/// <summary>
/// <see cref="IAppEdition"/> from Velopack's view of the running program (read after <c>VelopackApp.Run()</c> in <c>Program.Main</c>): a copy
/// that Setup installed has a recorded version and an install folder and is not Velopack's own portable zip (which is not built);
/// everything else, including a build from source and every test, is the portable edition.
/// </summary>
public sealed class VelopackEdition : IAppEdition
{
    /// <summary>What Velopack found: the installed version (null = not installed), its own portable zip, the install folder, the taskbar id.</summary>
    public VelopackEdition(string? installedVersion, bool velopackPortable, string? installRoot, string? appUserModelId)
    {
        if (!string.IsNullOrEmpty(installedVersion) && !velopackPortable && !string.IsNullOrEmpty(installRoot))
        {
            Kind = AppEditionKind.Installed;
            InstalledVersion = installedVersion;
            InstallRoot = installRoot;
            AppUserModelId = appUserModelId;
        }
    }

    /// <inheritdoc />
    public AppEditionKind Kind { get; }

    /// <inheritdoc />
    public string InstalledVersion { get; } = string.Empty;

    /// <inheritdoc />
    public string? InstallRoot { get; }

    /// <inheritdoc />
    public string? AppUserModelId { get; }

    /// <summary>The edition of the running program (the portable one when Velopack did not set itself up in this process).</summary>
    public static VelopackEdition Current()
    {
        var locator = VelopackLocator.IsCurrentSet ? VelopackLocator.Current : null;
        return new VelopackEdition(locator?.CurrentlyInstalledVersion?.ToString(), locator?.IsPortable ?? false, locator?.RootAppDir, locator?.AppUserModelId);
    }
}
