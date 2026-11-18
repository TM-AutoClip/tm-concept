using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace TMConcept.Desktop.Updates;

/// <summary>
/// The installed edition on this computer as the portable program sees it: Velopack's per-user install folder
/// (<c>%LOCALAPPDATA%\TMConcept</c>) with its <c>Update.exe</c> and the version recorded in <c>current\sq.version</c>. The portable program
/// does not offer the install over it (a user who installed through Setup from the browser and later runs a kept portable copy is never asked,
/// and an older Setup is never run over a newer install). Only read, never changed.
/// </summary>
public sealed class InstalledCopy(ILogger<InstalledCopy> logger)
{
    /// <summary>The install folder (tests give a temporary one).</summary>
    public string Root { get; init; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ReleaseAssetNames.PackageId);

    /// <summary>The installed version; null when TM Concept is not installed here, <c>unknown</c> when its record cannot be read.</summary>
    public string? Version()
    {
        var record = Path.Combine(Root, "current", "sq.version");
        if (!OperatingSystem.IsWindows() || !File.Exists(Path.Combine(Root, "Update.exe")) || !File.Exists(record))
        {
            return null;
        }

        try
        {
            var version = XDocument.Load(record).Descendants().FirstOrDefault(e => e.Name.LocalName == "version")?.Value.Trim();
            return string.IsNullOrEmpty(version) ? "unknown" : version;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
        {
            // Installed, but its record is unreadable: it counts as installed (an unknown version is never installed over).
            logger.LogWarning(ex, "The installed copy's version record could not be read");
            return "unknown";
        }
    }
}
