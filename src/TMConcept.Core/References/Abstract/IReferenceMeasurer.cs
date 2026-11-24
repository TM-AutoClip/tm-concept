namespace TMConcept.Core.References;

/// <summary>
/// Measures a reference video into a <see cref="ReferenceTempoProfile"/> ("bu videonun temposunda yap", S1, 0.1.52): a file of the user, or a
/// web address downloaded by yt-dlp into a temporary folder that is deleted afterwards, whatever happens. Only numbers are kept. Never during a
/// video's production; the template editor calls it.
/// </summary>
public interface IReferenceMeasurer
{
    /// <summary>The downloader (yt-dlp) is installed.</summary>
    bool DownloaderReady { get; }

    /// <summary>Measures the user's file <paramref name="file"/> (its name only goes into the profile).</summary>
    Task<ReferenceTempoProfile> MeasureFileAsync(string file, IProgress<ReferenceProgress>? progress, CancellationToken cancellationToken);

    /// <summary>Downloads and measures <paramref name="address"/> (an http or https address of one video).</summary>
    Task<ReferenceTempoProfile> MeasureAddressAsync(string address, IProgress<ReferenceProgress>? progress, CancellationToken cancellationToken);
}
