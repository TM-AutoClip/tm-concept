using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Components;
using TMConcept.Core.Music;
using TMConcept.Core.References;
using TMConcept.Core.Styles;

namespace TMConcept.Desktop.ViewModels;

/// <inheritdoc cref="IVideoMediaServices" />
public sealed class VideoMediaServices(Startup.FileDialogs dialogs, IMusicLibrary library, IMusicSearch search, Components.ComponentInstallFlow installs,
    IMusicGenerators generators, Core.Hardware.IGpuInfo gpu, IReferenceMeasurer measurer, IActivityLog activity, ILogger<VideoMediaServices> logger)
    : IVideoMediaServices
{
    private LocalMusicCard? _card;

    /// <inheritdoc />
    public MusicPickerServices Picker => new(library, search, InstallLibraryAsync, activity);

    /// <inheritdoc />
    public ReferenceVideoServices Reference => new(measurer,
        () => dialogs.OpenAsync("styles.reference.pick", "styles.reference.filter", ReferenceLimits.VideoFilePatterns), EnsureReferenceAsync, activity);

    /// <inheritdoc />
    public Task<string?> PickMusicFileAsync() => dialogs.OpenAsync("styles.music.pick", "styles.music.filter", MusicSources.FilePatterns);

    /// <inheritdoc />
    public async Task<MusicGeneratorServices> GeneratorsAsync()
    {
        _card ??= await Task.Run(() => LocalMusicSupport.Of(gpu.Detect(), OperatingSystem.IsLinux())).ConfigureAwait(true);
        return new MusicGeneratorServices(LocalMusicSupport.Ordered(generators.Models, _card), generators.Ready, _card);
    }

    /// <summary>Installs the ready music library (one confirmation). True when the library is ready afterwards.</summary>
    private async Task<bool> InstallLibraryAsync()
    {
        await InstallAsync([ComponentCodes.MusicLibrary], "install.purpose.music", "the music library").ConfigureAwait(true);
        return library.Ready;
    }

    /// <summary>Installs what a reference measure needs: ffmpeg, and yt-dlp for an address (one confirmation). True when ready afterwards.</summary>
    private Task<bool> EnsureReferenceAsync(bool address) =>
        InstallAsync(address ? [ComponentCodes.Ffmpeg, ComponentCodes.VideoDownloader] : [ComponentCodes.Ffmpeg], "install.purpose.reference",
            "the reference measure's tools");

    /// <summary>One confirmation of the app's install flow; a reason for not installing goes to the log and the activity panel.</summary>
    private async Task<bool> InstallAsync(IReadOnlyCollection<string> codes, string purposeKey, string what)
    {
        var outcome = await installs.EnsureAsync(codes, purposeKey, null, CancellationToken.None).ConfigureAwait(true);
        if (!outcome.Ready && outcome.Reason.Length > 0)
        {
            logger.LogInformation("{What} not installed: {Reason}", what, outcome.Reason);
            activity.Add(ActivityLevel.Warning, outcome.Reason);
        }

        return outcome.Ready;
    }
}
