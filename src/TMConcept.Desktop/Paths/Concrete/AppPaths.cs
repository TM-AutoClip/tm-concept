using TMConcept.Core.Paths;
using TMConcept.Desktop.Os;

namespace TMConcept.Desktop.Paths;

/// <summary>
/// Data folder: the given folder (portable mode), otherwise %LOCALAPPDATA%\TM Concept on Windows (not roaming, not synced) and
/// $XDG_DATA_HOME/tm-concept (~/.local/share/tm-concept) on Linux. Videos and sample images: Documents/TM Concept (the operating system's
/// documents folder), or <c>library</c> inside the data folder in portable mode.
/// </summary>
public sealed class AppPaths : IAppPaths
{
    /// <summary>Uses <paramref name="dataDirectory"/> or the default folders.</summary>
    public AppPaths(string? dataDirectory, IOsModule os)
    {
        ArgumentNullException.ThrowIfNull(os);
        IsPortable = dataDirectory is not null;
        DataDirectory = dataDirectory ?? os.DefaultDataDirectory();
        LibraryDirectory = dataDirectory is null ? Path.Combine(os.DocumentsDirectory(), "TM Concept") : Path.Combine(dataDirectory, "library");
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogDirectory);
    }

    /// <inheritdoc />
    public string DataDirectory { get; }

    /// <summary>Whether the data folder was given (<c>--data-dir</c>, portable mode).</summary>
    public bool IsPortable { get; }

    /// <summary>Problem report zips ("Report a problem"); only the newest five are kept.</summary>
    public string ReportDirectory => Path.Combine(DataDirectory, "reports");

    /// <inheritdoc />
    public string DatabaseFile => Path.Combine(DataDirectory, "concept.db");

    /// <inheritdoc />
    public string KeyFile => Path.Combine(DataDirectory, "key.json");

    /// <inheritdoc />
    public string LogDirectory => Path.Combine(DataDirectory, "logs");

    /// <inheritdoc />
    public string BackupDirectory => Path.Combine(DataDirectory, "backups");

    /// <inheritdoc />
    public string ToolsDirectory => Path.Combine(DataDirectory, "tools");

    /// <inheritdoc />
    public string WorkDirectory => Path.Combine(DataDirectory, "work");

    /// <inheritdoc />
    public string LibraryDirectory { get; }

    /// <summary>Plain preferences readable before the database is unlocked (only the interface language).</summary>
    public string PreferencesFile => Path.Combine(DataDirectory, "preferences.json");
}
