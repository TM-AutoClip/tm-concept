using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Desktop.Views;

/// <summary>
/// Generate concepts page. Keys on the list: A approves, D (or Delete) moves to the trash without a reason, ↑↓ move. 🗑 on a card opens a
/// small menu: "I did not like the topic", "I did not like the telling", "Just delete" (the reason goes to the prompts as a dislike).
/// </summary>
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

    /// <summary>
    /// Opens the trash menu of the clicked card. Built here with its commands and card set directly: a flyout lives in its own popup,
    /// where a binding to the list's data context would not reach.
    /// </summary>
    private void OnTrashClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ConceptCardViewModel card } button || DataContext is not ConceptsViewModel page)
        {
            return;
        }

        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        menu.Items.Add(Item("concepts.trash.topic", page.TrashTopicCommand, card));
        menu.Items.Add(Item("concepts.trash.telling", page.TrashTellingCommand, card));
        menu.Items.Add(Item("concepts.trash.just", page.TrashCommand, card));
        FlyoutBase.SetAttachedFlyout(button, menu);
        FlyoutBase.ShowAttachedFlyout(button);
        e.Handled = true;
    }

    private MenuItem Item(string key, ICommand command, ConceptCardViewModel card) => new()
    {
        Header = this.TryFindResource(key, out var text) ? text : key,
        Command = command,
        CommandParameter = card,
    };
}
