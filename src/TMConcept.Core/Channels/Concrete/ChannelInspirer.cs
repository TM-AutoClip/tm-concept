using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Paths;
using TMConcept.Core.References;

namespace TMConcept.Core.Channels;

/// <summary>
/// <see cref="IChannelInspirer"/>: every look gets its own empty folder under <c>work/channel/</c> (yt-dlp runs there, the channel photo lands
/// there). The public details come from <see cref="IChannelLister"/>; a channel's home that lists only its tabs is read again through its first
/// <see cref="ChannelLimits.TabsMax"/> tabs. The samples (<see cref="ChannelSamplePicker"/>) are measured one after the other by the reference
/// measure (<see cref="IReferenceMeasurer.MeasureAddressAsync"/>: its own folder under <c>work/reference/</c>, the download deleted in its
/// <c>finally</c>); a sample that fails is skipped and counted, a cancel stops everything. The measured samples are merged
/// (<see cref="TempoProfileMerger"/>) and the facts come from the listing (<see cref="ChannelDraftBuilder"/>). A failure or a cancel of the look
/// deletes its folder at once; after a success the folder (the photo) stays until <see cref="Discard"/>; the start-up sweep removes what a
/// closed app left (<see cref="ActiveFolders"/> are never swept). Only steps, hosts and counts are logged.
/// </summary>
public sealed class ChannelInspirer(IAppPaths paths, IChannelLister lister, IReferenceMeasurer measurer, IChannelPhotoFetcher photos, TimeProvider clock,
    ILogger<ChannelInspirer> logger) : IChannelInspirer
{
    private readonly ConcurrentDictionary<string, byte> _active = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The folder every look's own folder is made in.</summary>
    public string Root => Path.Combine(paths.WorkDirectory, ChannelLimits.Folder);

    /// <inheritdoc />
    public IReadOnlyCollection<string> ActiveFolders => [.. _active.Keys];

    /// <inheritdoc />
    public bool Ready => lister.Ready && measurer.DownloaderReady;

    /// <inheritdoc />
    public async Task<ChannelInspiration> InspectAsync(string address, int samples, bool photo, IProgress<ChannelProgress>? progress,
        CancellationToken cancellationToken)
    {
        var uri = ReferenceMeasurer.Address(address) ?? throw new ChannelInspireException("The channel is not an http or https address.") { Code = "address" };
        if (!lister.Ready)
        {
            throw new ChannelInspireException("yt-dlp is not installed.") { Code = "missing" };
        }

        var folder = Path.Combine(Root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        _active[folder] = 0;
        try
        {
            progress?.Report(new ChannelProgress(ChannelStages.List, 0, 0, 0));
            var listing = await ListAsync(uri, folder, cancellationToken).ConfigureAwait(false);
            if (listing.Videos.Count == 0)
            {
                throw new ChannelInspireException("No video of the channel was listed.") { Code = "empty" };
            }

            var picks = ChannelSamplePicker.Pick(listing.Videos, samples);
            var measured = new List<ReferenceTempoProfile>();
            var failed = 0;
            for (var i = 0; i < picks.Count; i++)
            {
                var index = i + 1;
                progress?.Report(new ChannelProgress(ChannelStages.Sample, index, picks.Count, 0));
                var share = new SampleProgress(progress, index, picks.Count);
                try
                {
                    measured.Add(await measurer.MeasureAddressAsync(picks[i].Url, share, cancellationToken).ConfigureAwait(false));
                }
                catch (Exception ex) when ((ex is ReferenceMeasureException { Code: not "missing" } or IOException or InvalidOperationException
                    or System.ComponentModel.Win32Exception) && ex is not OperationCanceledException)
                {
                    // One sample that cannot be downloaded or read (a members-only video, a removed one) does not stop the look.
                    failed++;
                    logger.LogWarning("Channel sample {Index} of {Count} could not be measured ({Code})", index, picks.Count,
                        ex is ReferenceMeasureException measure ? measure.Code : ex.GetType().Name);
                }
            }

            progress?.Report(new ChannelProgress(ChannelStages.Draft, 0, 0, 0));
            var tempo = measured.Count > 0 ? TempoProfileMerger.Merge(measured, uri.AbsoluteUri, clock.GetUtcNow()) : null;
            var facts = ChannelDraftBuilder.Facts(listing);
            string? photoFile = null;
            if (photo && Uri.TryCreate(listing.PhotoUrl, UriKind.Absolute, out var photoUri))
            {
                progress?.Report(new ChannelProgress(ChannelStages.Photo, 0, 0, 0));
                photoFile = await PhotoAsync(photoUri, folder, cancellationToken).ConfigureAwait(false);
            }

            logger.LogInformation(
                "Channel looked at on {Host}: {Videos} videos listed, {Measured} of {Samples} samples measured ({Failed} failed), length {Seconds}, format {Format}, language {Language}, photo {Photo}",
                uri.Host, listing.Videos.Count, measured.Count, picks.Count, failed, facts.TargetSeconds?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-",
                facts.Format?.ToString() ?? "-", facts.Language ?? "-", photoFile is null ? "no" : "yes");
            return new ChannelInspiration(listing, facts, tempo, measured.Count, failed, photoFile, folder);
        }
        catch
        {
            Remove(folder);
            throw;
        }
    }

    /// <inheritdoc />
    public void Discard(ChannelInspiration inspiration)
    {
        ArgumentNullException.ThrowIfNull(inspiration);
        if (inspiration.Folder.Length > 0)
        {
            Remove(inspiration.Folder);
        }
    }

    private async Task<ChannelListing> ListAsync(Uri uri, string folder, CancellationToken cancellationToken)
    {
        var listing = await lister.ListAsync(uri, ChannelLimits.ListCount, folder, cancellationToken).ConfigureAwait(false);
        if (listing.Videos.Count > 0 || listing.Tabs.Count == 0)
        {
            return listing;
        }

        // A channel's home lists its tabs (videos, shorts…): their videos are read, newest of each first, together at most ListCount.
        var videos = new List<ChannelVideo>();
        foreach (var tab in listing.Tabs.Take(ChannelLimits.TabsMax))
        {
            if (videos.Count >= ChannelLimits.ListCount || ReferenceMeasurer.Address(tab) is not { } tabUri)
            {
                break;
            }

            ChannelListing part;
            try
            {
                part = await lister.ListAsync(tabUri, ChannelLimits.ListCount - videos.Count, folder, cancellationToken).ConfigureAwait(false);
            }
            catch (ChannelInspireException ex) when (ex.Code == "list" && videos.Count > 0)
            {
                // A later tab that cannot be read (a Shorts tab yt-dlp does not answer for) does not throw away the videos already read.
                logger.LogWarning("A tab of the channel's home could not be read; the {Videos} videos read so far are kept", videos.Count);
                break;
            }

            videos.AddRange(part.Videos.Where(v => videos.All(o => o.Id != v.Id)));
        }

        logger.LogInformation("The channel's home listed {Tabs} tabs; {Videos} videos read from them", listing.Tabs.Count, videos.Count);
        return listing with { Videos = videos, Tabs = [] };
    }

    private async Task<string?> PhotoAsync(Uri photo, string folder, CancellationToken cancellationToken)
    {
        try
        {
            return await photos.FetchAsync(photo, folder, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException
            || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // No photo is no failure: the user may pick one on the profile page.
            logger.LogWarning("The channel photo could not be fetched ({Error})", ex.GetType().Name);
            return null;
        }
    }

    private void Remove(string folder)
    {
        _active.TryRemove(folder, out _);
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("The channel look's work folder could not be removed yet ({Error}); it is removed at the next start", ex.GetType().Name);
        }
    }

    /// <summary>A sample's measure progress as the look's progress (the sample's number with the measure's own share).</summary>
    private sealed class SampleProgress(IProgress<ChannelProgress>? progress, int index, int count) : IProgress<ReferenceProgress>
    {
        public void Report(ReferenceProgress value)
        {
            ArgumentNullException.ThrowIfNull(value);
            progress?.Report(new ChannelProgress(ChannelStages.Sample, index, count, value.Stage == ReferenceStages.Download ? value.Fraction * 0.8 : 0.9));
        }
    }
}
