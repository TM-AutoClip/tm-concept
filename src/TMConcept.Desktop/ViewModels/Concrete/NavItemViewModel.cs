using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TMConcept.Desktop.ViewModels;

/// <summary>A sidebar entry.</summary>
public sealed partial class NavItemViewModel(ShellViewModel shell, PageViewModelBase page) : ObservableObject
{
    /// <summary>Page.</summary>
    public PageViewModelBase Page { get; } = page;

    /// <summary>Title in the current language.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>Glyph.</summary>
    public string Glyph => Page.Glyph;

    /// <summary>Whether this page is shown.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Opens the page.</summary>
    [RelayCommand]
    private Task OpenAsync() => shell.NavigateAsync(Page);
}
