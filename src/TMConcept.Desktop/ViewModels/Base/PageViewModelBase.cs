using CommunityToolkit.Mvvm.ComponentModel;

namespace TMConcept.Desktop.ViewModels;

/// <summary>A page of the main window. The sidebar lists every registered page by <see cref="Order"/>.</summary>
public abstract partial class PageViewModelBase : ObservableObject
{
    /// <summary>Localization key of the title.</summary>
    public abstract string TitleKey { get; }

    /// <summary>A single glyph shown before the title.</summary>
    public abstract string Glyph { get; }

    /// <summary>Position in the sidebar.</summary>
    public abstract int Order { get; }

    /// <summary>Status line of the page.</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    /// <summary>A short badge next to the sidebar title (for example the trash count); empty = none.</summary>
    [ObservableProperty]
    public partial string Badge { get; set; } = string.Empty;

    /// <summary>
    /// The page runs paid or long work (a generation, a video queue). While any page works the profile cannot be switched (the work belongs
    /// to the selected profile) and closing the window asks first.
    /// </summary>
    [ObservableProperty]
    public partial bool Working { get; protected set; }

    /// <summary>Asks the running work to stop (after its running step); nothing when the page has no work.</summary>
    public virtual void CancelWork()
    {
    }

    /// <summary>Called whenever the page is shown or the selected profile changes while it is shown (reload data here).</summary>
    public virtual Task ActivateAsync() => Task.CompletedTask;

    /// <summary>Called when another page is opened (stop what only makes sense on screen, such as a playing video).</summary>
    public virtual void Deactivate()
    {
    }

    /// <summary>Called when the selected profile changed, whether or not the page is shown (update badges here).</summary>
    public virtual Task ProfileChangedAsync() => Task.CompletedTask;
}
