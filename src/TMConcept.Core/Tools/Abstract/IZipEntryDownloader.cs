namespace TMConcept.Core.Tools;

/// <summary>
/// Downloads one pinned entry of a published zip (<see cref="ZipEntryArchive"/>, 0.1.27) with the rules of every download (HTTPS, allowed
/// hosts on every hop, a few tries): the unpacked file is moved into place only when its SHA-256 matches; a mismatch deletes it
/// (<see cref="ToolInstallException"/> code <c>hash</c>).
/// </summary>
public interface IZipEntryDownloader
{
    /// <summary>Downloads <paramref name="entry"/> to <paramref name="file"/>.</summary>
    Task DownloadAsync(ZipEntryArchive entry, string file, IProgress<ToolProgress>? progress, CancellationToken cancellationToken);
}
