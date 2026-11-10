using Avalonia.Controls;
using Avalonia.Input;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Desktop.Views;

/// <summary>Generate concepts page. Keys on the list: A approves, D (or Delete) moves to the trash, ↑↓ move.</summary>
public sealed partial class ConceptsView : UserControl
{
    /// <summary>Creates.</summary>
    public ConceptsView() => InitializeComponent();

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ConceptsViewModel page || page.Selected is not { } card)
        {
            return;
        }

        if (e.Key == Key.A)
        {
            page.ApproveCommand.Execute(card);
            e.Handled = true;
        }
        else if (e.Key is Key.D or Key.Delete)
        {
            page.TrashCommand.Execute(card);
            e.Handled = true;
        }
    }
}
