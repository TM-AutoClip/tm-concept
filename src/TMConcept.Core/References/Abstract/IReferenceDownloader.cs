namespace TMConcept.Core.References;

/// <summary>
/// Downloads one reference video from a web address (S1, 0.1.52; yt-dlp, a component installed only after the user's yes): a single video,
/// no playlist, no cookies or sign-in, at most <see cref="ReferenceLimits.MaxHeight"/> pixels high and the first
/// <see cref="ReferenceLimits.MaxSeconds"/> seconds. Called only when the user measures an address.
/// </summary>
public interface IReferenceDownloader
{
    /// <summary>yt-dlp is installed.</summary>
    bool Ready { get; }

    /// <summary>
    /// Downloads <paramref name="address"/> into <paramref name="folder"/> (empty; it belongs to the caller, who deletes it) and returns the
    /// file; <paramref name="progress"/> gets 0–1. Throws <see cref="ReferenceMeasureException"/> ("missing", "download").
    /// </summary>
    Task<string> DownloadAsync(Uri address, string folder, IProgress<double>? progress, CancellationToken cancellationToken);
}
