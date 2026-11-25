using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Components;
using TMConcept.Core.Music;
using TMConcept.Core.Styles;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// What the music and reference video choices need (0.1.54; before, the template editor built these itself): the file dialogs, the ready
/// library with the Openverse search and its install, the music models with the local model's install and how it fits this card (read once,
/// off the window's thread), the reference measure with what it needs installed, and "Müziklerim". Every install goes through the app's
/// install flow (one confirmation; nothing is downloaded without the yes). Shared by the video page's model choice and the music page.
/// </summary>
public sealed class VideoMediaServices(Startup.FileDialogs dialogs, IMusicLibrary library, IMusicSearch search, Components.ComponentInstallFlow installs,
    IMusicGenerators generators, Core.Hardware.IGpuInfo gpu, Core.References.IReferenceMeasurer measurer, IActivityLog activity, IMyMusic myMusic,
    ILogger<VideoMediaServices> logger)
{
    /// <summary>The video files a reference may be (ffmpeg reads them).</summary>
    public static readonly IReadOnlyList<string> ReferenceVideoFiles = ["*.mp4", "*.mov", "*.mkv", "*.webm", "*.avi", "*.m4v"];

    private LocalMusicCard? _card;

    /// <summary>The user's own music collection.</summary>
    public IMyMusic MyMusic => myMusic;

    /// <summary>The music models.</summary>
    public IMusicGenerators Generators => generators;

    /// <summary>The activity panel.</summary>
    public IActivityLog Activity => activity;

    /// <summary>The library music's services.</summary>
    public MusicPickerServices Picker => new(library, search, InstallLibraryAsync, activity);

    /// <summary>The reference video's services.</summary>
    public ReferenceVideoServices Reference => new(measurer, () => dialogs.OpenAsync("styles.reference.pick", "styles.reference.filter", ReferenceVideoFiles),
        EnsureReferenceAsync, activity);

    /// <summary>Asks for an audio file (null when canceled).</summary>
    public Task<string?> PickMusicFileAsync() => dialogs.OpenAsync("styles.music.pick", "styles.music.filter", MusicSources.FilePatterns);

    /// <summary>The generated music's services (how the local model fits this card is read once, off the window's thread).</summary>
    public async Task<MusicGeneratorServices> GeneratorsAsync()
    {
        _card ??= await Task.Run(() => LocalMusicSupport.Of(gpu.Detect(), OperatingSystem.IsLinux())).ConfigureAwait(true);
        return new MusicGeneratorServices(generators.Models, generators.Ready, _card, InstallLocalMusicAsync);
    }

    /// <summary>Installs the local music model (its environment and weights; one confirmation). True when it is ready afterwards.</summary>
    public async Task<bool> InstallLocalMusicAsync()
    {
        var outcome = await installs.EnsureAsync([ComponentCodes.MusicModel], "install.purpose.music_model", null, CancellationToken.None).ConfigureAwait(true);
        if (!outcome.Ready && outcome.Reason.Length > 0)
        {
            logger.LogInformation("The local music model was not installed: {Reason}", outcome.Reason);
            activity.Add(ActivityLevel.Warning, outcome.Reason);
        }

        return outcome.Ready;
    }

    /// <summary>Installs ffmpeg (a reference sound is read with it; one confirmation). True when it is ready afterwards.</summary>
    public async Task<bool> EnsureFfmpegAsync()
    {
        var outcome = await installs.EnsureAsync([ComponentCodes.Ffmpeg], "install.purpose.reference", null, CancellationToken.None).ConfigureAwait(true);
        return outcome.Ready;
    }

    /// <summary>Installs the ready music library (one confirmation). True when the library is ready afterwards.</summary>
    private async Task<bool> InstallLibraryAsync()
    {
        var outcome = await installs.EnsureAsync([ComponentCodes.MusicLibrary], "install.purpose.music", null, CancellationToken.None).ConfigureAwait(true);
        if (!outcome.Ready && outcome.Reason.Length > 0)
        {
            logger.LogInformation("The music library was not installed: {Reason}", outcome.Reason);
            activity.Add(ActivityLevel.Warning, outcome.Reason);
        }

        return library.Ready;
    }

    /// <summary>Installs what a reference measure needs: ffmpeg, and yt-dlp for an address (one confirmation). True when ready afterwards.</summary>
    private async Task<bool> EnsureReferenceAsync(bool address)
    {
        string[] codes = address ? [ComponentCodes.Ffmpeg, ComponentCodes.VideoDownloader] : [ComponentCodes.Ffmpeg];
        var outcome = await installs.EnsureAsync(codes, "install.purpose.reference", null, CancellationToken.None).ConfigureAwait(true);
        if (!outcome.Ready && outcome.Reason.Length > 0)
        {
            logger.LogInformation("The reference measure's tools were not installed: {Reason}", outcome.Reason);
        }

        return outcome.Ready;
    }
}
