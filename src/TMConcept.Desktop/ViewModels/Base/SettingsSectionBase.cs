using CommunityToolkit.Mvvm.ComponentModel;

namespace TMConcept.Desktop.ViewModels;

/// <summary>A tab of the settings page. Sections come from DI (a new section = a new class).</summary>
public abstract partial class SettingsSectionBase : ObservableObject
{
    /// <summary>Localization key of the tab title.</summary>
    public abstract string TitleKey { get; }

    /// <summary>Position of the tab.</summary>
    public abstract int Order { get; }

    /// <summary>Tab title in the current language.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>Status line of the section.</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    /// <summary>Called whenever the section is shown.</summary>
    public virtual Task ActivateAsync() => Task.CompletedTask;
}
