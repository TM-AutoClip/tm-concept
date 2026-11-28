using System.Text.Json;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Components;
using TMConcept.Desktop.Paths;

namespace TMConcept.Desktop.Components;

/// <summary>
/// <see cref="IInstallRecord"/> as <c>installed-components.json</c> in the data folder, next to the components folder and not inside it:
/// "Reinstall from scratch" deletes the components folder first, a download may be declined or fail, and the record still says what to install
/// again (with the app version each component was installed with), so the question after a new version comes back until it is done.
/// </summary>
public sealed class InstallRecordFile(AppPaths paths, string currentVersion, TimeProvider clock, ILogger<InstallRecordFile> logger) : IInstallRecord
{
    private readonly Lock _gate = new();

    /// <inheritdoc />
    public IReadOnlyList<InstalledComponent> List()
    {
        lock (_gate)
        {
            return Read();
        }
    }

    /// <inheritdoc />
    public void Installed(string code, string? appVersion = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        lock (_gate)
        {
            var entries = Read();
            var version = appVersion ?? currentVersion;
            var existing = entries.FirstOrDefault(e => e.Code == code);
            if (existing is not null && version.Length == 0)
            {
                return;
            }

            entries.RemoveAll(e => e.Code == code);
            entries.Add(new InstalledComponent { Code = code, AppVersion = version, InstalledAtUtc = clock.GetUtcNow() });
            Write(entries);
            logger.LogInformation("Install record: {Component} (app version {Version})", code, version.Length > 0 ? version : "unknown, found on disk");
        }
    }

    /// <inheritdoc />
    public void Removed(string code)
    {
        lock (_gate)
        {
            var entries = Read();
            if (entries.RemoveAll(e => e.Code == code) > 0)
            {
                Write(entries);
                logger.LogInformation("Install record: {Component} removed", code);
            }
        }
    }

    private List<InstalledComponent> Read()
    {
        if (!File.Exists(paths.InstalledComponentsFile))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(paths.InstalledComponentsFile), DesktopJsonContext.Default.ListInstalledComponent) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // An unreadable record: what is on disk still counts (the planner adds it again).
            logger.LogWarning(ex, "The install record could not be read; it starts again from what is on disk");
            return [];
        }
    }

    private void Write(List<InstalledComponent> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(paths.InstalledComponentsFile)!);
        var temporary = paths.InstalledComponentsFile + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(entries, DesktopJsonContext.Default.ListInstalledComponent));
        File.Move(temporary, paths.InstalledComponentsFile, overwrite: true);
    }
}
