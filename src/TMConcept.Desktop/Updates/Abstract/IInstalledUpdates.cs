namespace TMConcept.Desktop.Updates;

/// <summary>
/// The installed edition's updates (Velopack, from the project's GitHub releases; tests give a fake): what is newer, its download (the small
/// delta packages when the previous version is on disk, otherwise the full package) and the apply after the app has closed.
/// </summary>
public interface IInstalledUpdates
{
    /// <summary>The newer version and the package files Velopack would download; null when there is none.</summary>
    Task<InstalledUpdate?> CheckAsync(CancellationToken cancellationToken);

    /// <summary>Downloads (and checks) <paramref name="update"/>'s packages into the install folder; <paramref name="percent"/> gets 0–100.</summary>
    Task DownloadAsync(InstalledUpdate update, Action<int> percent, CancellationToken cancellationToken);

    /// <summary>
    /// Starts Velopack's updater, which waits until this app has ended, applies <paramref name="update"/> and starts the new version (with
    /// <paramref name="restartArguments"/>). The caller closes the app right after.
    /// </summary>
    void ApplyAfterExit(InstalledUpdate update, IReadOnlyList<string> restartArguments);
}
