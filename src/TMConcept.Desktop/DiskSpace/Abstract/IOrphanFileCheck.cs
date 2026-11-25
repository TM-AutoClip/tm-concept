namespace TMConcept.Desktop.DiskSpace;

/// <summary>
/// The start's look for unused model files (0.1.54, the developer: "eski dosyalar silinmiyor, disk şişiyor"): after the start-up sweep, the
/// files of the components and models folders that no component owns (<see cref="DiskSelection.Orphans"/>) are added up; from
/// <see cref="OrphanFileCheck.DefaultAskFromBytes"/> on, a window lists them with their sizes and deletes them for good only on the user's yes. Asked
/// at most once a day; never while work or an install runs. Settings → Storage → "Kullanılmayan model dosyalarını temizle" does the same at any time.
/// </summary>
public interface IOrphanFileCheck
{
    /// <summary>Looks and, when there is enough, asks (on the window's thread); returns the bytes freed (0 when nothing was asked or the user said no).</summary>
    Task<long> RunAsync(CancellationToken cancellationToken);
}
