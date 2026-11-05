namespace TMConcept.Core.Paths;

/// <summary>
/// Where the app keeps its files. Data (encrypted database, key file, backups, logs, tools, work files) lives in one data folder
/// (<c>%LOCALAPPDATA%\TM Concept</c> on Windows, <c>~/.local/share/tm-concept</c> on Linux, or <c>--data-dir</c>). Videos and sample images
/// live unencrypted in the user's documents folder (<c>Documents/TM Concept</c>). Every path in the code comes from here.
/// </summary>
public interface IAppPaths
{
    /// <summary>Data folder.</summary>
    string DataDirectory { get; }

    /// <summary>Encrypted database file.</summary>
    string DatabaseFile { get; }

    /// <summary>File that holds the protected database key and how it is protected.</summary>
    string KeyFile { get; }

    /// <summary>Log folder.</summary>
    string LogDirectory { get; }

    /// <summary>Backup folder (the last ten daily backups).</summary>
    string BackupDirectory { get; }

    /// <summary>Downloaded tools (Node.js, ffmpeg, Chrome, engine packages, voice models).</summary>
    string ToolsDirectory { get; }

    /// <summary>Work files of the video pipeline (code, engine projects, intermediate audio); can be cleaned.</summary>
    string WorkDirectory { get; }

    /// <summary>Videos and sample images (unencrypted, in the user's documents folder).</summary>
    string LibraryDirectory { get; }
}
