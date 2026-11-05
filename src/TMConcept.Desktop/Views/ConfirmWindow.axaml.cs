using Avalonia.Controls;
using TMConcept.Desktop.Dialogs;

namespace TMConcept.Desktop.Views;

/// <summary>A confirmation window.</summary>
public sealed partial class ConfirmWindow : Window
{
    /// <summary>Creates.</summary>
    public ConfirmWindow()
    {
        InitializeComponent();
        CancelButton.Click += (_, _) => Close();
        ConfirmButton.Click += (_, _) => Confirm();
        DangerButton.Click += (_, _) => Confirm();
    }

    private void Confirm()
    {
        if (DataContext is ConfirmViewModel model)
        {
            model.Confirmed = true;
        }

        Close();
    }
}
