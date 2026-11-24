namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// What the "Kanaldan esinlen" window needs (0.1.53): the look, the one model call, the template draft, the install of ffmpeg and yt-dlp through
/// the app's install flow (one confirmation; true when ready), the text model of the selected profile's concept step (null when none is chosen
/// or it is not ready: then only the numbers are drafted), the activity panel and the clock.
/// </summary>
/// <param name="Inspirer">The look at the channel.</param>
/// <param name="Writer">The one call that writes the channel content.</param>
/// <param name="Templates">Adds the template draft.</param>
/// <param name="EnsureAsync">Installs ffmpeg and yt-dlp after one confirmation; true when ready.</param>
/// <param name="ModelAsync">The chosen text model, or null.</param>
/// <param name="Activity">The activity panel.</param>
/// <param name="Clock">The clock.</param>
public sealed record ChannelInspirationServices(Core.Channels.IChannelInspirer Inspirer, Core.Channels.IChannelStyleWriter Writer,
    Core.Channels.IChannelTemplateCreator Templates, Func<Task<bool>> EnsureAsync, Func<CancellationToken, Task<Core.Models.ResolvedModel?>> ModelAsync,
    Core.Activity.IActivityLog Activity, TimeProvider Clock);
