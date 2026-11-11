using Avalonia.Controls;

namespace TMConcept.Desktop.Views;

/// <summary>
/// The main window: the app bar (spending pill, page tabs, profile, report, settings), the current page and the activity log at the bottom
/// (<see cref="ViewModels.ShellViewModel"/>). Opens at 1280×720. Below <see cref="NarrowWidth"/> the window gets the "narrow" class: the app
/// name and the Settings title hide (the gear stays), the profile selector narrows and the tabs move closer, so every tab title still fits
/// down to the minimum width (1024).
/// </summary>
public sealed partial class MainWindow : Window
{
    /// <summary>Below this width the app bar compacts.</summary>
    public const double NarrowWidth = 1150;

    /// <summary>Creates.</summary>
    public MainWindow() => InitializeComponent();

    /// <inheritdoc />
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnSizeChanged(e);
        Classes.Set("narrow", e.NewSize.Width < NarrowWidth);
    }
}
