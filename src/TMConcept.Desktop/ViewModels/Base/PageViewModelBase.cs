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

    /// <summary>Called whenever the page is shown or the selected profile changes while it is shown (reload data here).</summary>
    public virtual Task ActivateAsync() => Task.CompletedTask;

    /// <summary>Called when the selected profile changed, whether or not the page is shown (update badges here).</summary>
    public virtual Task ProfileChangedAsync() => Task.CompletedTask;
}
