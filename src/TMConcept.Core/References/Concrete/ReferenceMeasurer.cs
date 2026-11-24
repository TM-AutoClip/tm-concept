using Microsoft.Extensions.Logging;
using TMConcept.Core.Music;
using TMConcept.Core.Paths;

namespace TMConcept.Core.References;

/// <summary>
/// The reference measure (S1, 0.1.52): every measure gets its own empty folder under <c>work/reference/</c>; an address is downloaded there by
/// yt-dlp (<see cref="IReferenceDownloader"/>), then ffmpeg reads the grey frames, the colour frames and the sound of the first
/// <see cref="ReferenceLimits.MaxSeconds"/> (<see cref="IReferenceVideoReader"/>, <see cref="IMusicAudio"/>) and
/// <see cref="ReferenceProfileBuilder"/> turns them into numbers. The folder (the download with it) is deleted in a <c>finally</c>: after a
/// success, a failure or a cancel; one that cannot be removed at once is removed at the next start (<see cref="Videos.VideoPipeline.CleanWork"/>).
/// A sound that cannot be read only leaves the sound out; a picture that cannot be read is an error.
/// </summary>
public sealed class ReferenceMeasurer(IAppPaths paths, IReferenceVideoReader reader, IMusicAudio audio, IReferenceDownloader downloader, TimeProvider clock,
    ILogger<ReferenceMeasurer> logger) : IReferenceMeasurer
{
    /// <inheritdoc />
    public bool DownloaderReady => downloader.Ready;

    /// <summary>The folder every measure's own folder is made in.</summary>
    public string Root => Path.Combine(paths.WorkDirectory, ReferenceLimits.Folder);

    /// <summary>An http or https address without a user name or password, or null.</summary>
    public static Uri? Address(string? text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            && uri.Host.Length > 0 && uri.UserInfo.Length == 0
            ? uri
            : null;
    }

    /// <inheritdoc />
    public async Task<ReferenceTempoProfile> MeasureFileAsync(string file, IProgress<ReferenceProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        if (!File.Exists(file))
        {
            throw new ReferenceMeasureException("The reference file is not there.") { Code = "file" };
        }

        var work = NewFolder();
        try
        {
            return await MeasureAsync(file, Path.GetFileName(file), fromUrl: false, work, progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Remove(work);
        }
    }

    /// <inheritdoc />
    public async Task<ReferenceTempoProfile> MeasureAddressAsync(string address, IProgress<ReferenceProgress>? progress, CancellationToken cancellationToken)
    {
        var uri = Address(address) ?? throw new ReferenceMeasureException("The reference is not an http or https address.") { Code = "address" };
        if (!downloader.Ready)
        {
            throw new ReferenceMeasureException("yt-dlp is not installed.") { Code = "missing" };
        }

        var work = NewFolder();
        try
        {
            progress?.Report(new ReferenceProgress(ReferenceStages.Download, 0));
            var download = Path.Combine(work, "download");
            Directory.CreateDirectory(download);
            var share = new Progress<double>(f => progress?.Report(new ReferenceProgress(ReferenceStages.Download, Math.Clamp(f, 0, 1))));
            var file = await downloader.DownloadAsync(uri, download, share, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Reference video downloaded from {Host} ({Megabytes:0.0} MB)", uri.Host, new FileInfo(file).Length / 1048576.0);
            return await MeasureAsync(file, uri.AbsoluteUri, fromUrl: true, work, progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Remove(work);
        }
    }

    private async Task<ReferenceTempoProfile> MeasureAsync(string video, string source, bool fromUrl, string work, IProgress<ReferenceProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new ReferenceProgress(ReferenceStages.Frames, 0));
        var frames = await reader.ReadFramesAsync(video, work, cancellationToken).ConfigureAwait(false)
            ?? throw new ReferenceMeasureException("ffmpeg could not read the reference's picture.") { Code = "unreadable" };
        if (frames.Seconds < ReferenceLimits.MinSeconds)
        {
            throw new ReferenceMeasureException("The reference is too short to measure.") { Code = "short" };
        }

        progress?.Report(new ReferenceProgress(ReferenceStages.Colors, 0));
        var colors = await reader.ReadColorsAsync(video, work, cancellationToken).ConfigureAwait(false);
        progress?.Report(new ReferenceProgress(ReferenceStages.Sound, 0));
        var samples = await audio.ReadMonoAsync(video, ReferenceLimits.SampleRate, ReferenceLimits.MaxSeconds, work, cancellationToken).ConfigureAwait(false);
        if (samples is null)
        {
            logger.LogInformation("The reference has no readable sound; it is measured without it");
        }

        progress?.Report(new ReferenceProgress(ReferenceStages.Done, 1));
        var profile = await Task.Run(() => ReferenceProfileBuilder.Build(new ReferenceMeasureInput(frames, colors, samples, ReferenceLimits.SampleRate), source,
            fromUrl, clock.GetUtcNow()), cancellationToken).ConfigureAwait(false);
        logger.LogInformation(
            "Reference measured: {Seconds:0} s, {Cuts} cuts ({Per10:0.##} in 10 s), camera {Camera:0.##} {Direction}, {Bpm:0.#} bpm, {Peaks} peaks",
            profile.Seconds, profile.Cuts, profile.CutsPer10Seconds, profile.CameraShare, profile.CameraDirection, profile.Bpm, profile.Peaks.Count);
        return profile;
    }

    private string NewFolder()
    {
        var folder = Path.Combine(Root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private void Remove(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A killed child can hold a file a moment longer: the next start removes the folder (VideoPipeline.CleanWork).
            logger.LogWarning("The reference measure's work folder could not be removed yet ({Error}); it is removed at the next start", ex.Message);
        }
    }
}
