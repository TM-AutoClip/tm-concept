using Avalonia.Controls;
using TMConcept.Desktop.Dialogs;

namespace TMConcept.Desktop.Views;

/// <summary>The window that follows a new app version (<see cref="UpdateDialog"/>).</summary>
public sealed partial class UpdateWindow : Window
{
    /// <summary>Creates.</summary>
    public UpdateWindow()
    {
        InitializeComponent();
        LaterButton.Click += (_, _) => Close();
        ReinstallButton.Click += (_, _) => Choose(UpdateChoice.Reinstall);
        UpdateButton.Click += (_, _) => Choose(UpdateChoice.Update);
    }

    private void Choose(UpdateChoice choice)
    {
        (DataContext as UpdateViewModel)?.Choose(choice);
        Close();
    }
}
