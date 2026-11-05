namespace TMConcept.Core.Tools;

/// <summary>Downloads a pinned archive and checks its SHA-256 (a mismatch deletes the file).</summary>
public interface IArchiveDownloader
{
    /// <summary>Downloads <paramref name="archive"/> to <paramref name="file"/>.</summary>
    Task DownloadAsync(ToolArchive archive, string file, IProgress<ToolProgress>? progress, CancellationToken cancellationToken);
}
