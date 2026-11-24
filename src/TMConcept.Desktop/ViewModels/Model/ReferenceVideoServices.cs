namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// What the template editor's reference video needs (S1, 0.1.52): the measure, the file dialog of a video file (<c>null</c> when canceled), the
/// install of what a measure needs through the app's install flow (ffmpeg; with yt-dlp for an address; one confirmation, nothing is
/// downloaded without the yes; true when ready afterwards) and the activity panel for errors.
/// </summary>
/// <param name="Measurer">The reference measure.</param>
/// <param name="PickFileAsync">Asks for a video file.</param>
/// <param name="EnsureAsync">Installs what a measure needs (true: for an address, with yt-dlp); true when it is ready.</param>
/// <param name="Activity">The activity panel.</param>
public sealed record ReferenceVideoServices(Core.References.IReferenceMeasurer Measurer, Func<Task<string?>> PickFileAsync, Func<bool, Task<bool>> EnsureAsync,
    Core.Activity.IActivityLog Activity);
