namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// What the template editor's library music needs (0.1.45): the library on this computer, the Openverse search, the install of the library
/// (one confirmation, the app's install flow; true when it is ready afterwards) and the activity panel for errors.
/// </summary>
/// <param name="Library">The ready library.</param>
/// <param name="Search">The Openverse search.</param>
/// <param name="InstallAsync">Installs the library after the user agreed; true when it is ready.</param>
/// <param name="Activity">The activity panel.</param>
public sealed record MusicPickerServices(Core.Music.IMusicLibrary Library, Core.Music.IMusicSearch Search, Func<Task<bool>> InstallAsync,
    Core.Activity.IActivityLog Activity);
