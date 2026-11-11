using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Desktop.Views;

/// <summary>
/// The concept detail panel. Delete opens a small menu: "I did not like the topic", "I did not like the telling", "Just delete" (the reason
/// goes to the prompts as a dislike); the list rows' Delete is "just delete" at once.
/// </summary>
public sealed partial class ConceptDetailView : UserControl
{
    /// <summary>Creates.</summary>
    public ConceptDetailView() => InitializeComponent();

    /// <summary>
    /// Opens the delete menu. Built here with its commands set directly: a flyout lives in its own popup, where a binding to the panel's
    /// data context would not reach.
    /// </summary>
    private void OnTrashClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || DataContext is not ConceptDetailViewModel detail)
        {
            return;
        }

        var menu = new MenuFlyout { Placement = PlacementMode.TopEdgeAlignedLeft };
        menu.Items.Add(Item("concepts.trash.topic", detail.TrashTopicCommand));
        menu.Items.Add(Item("concepts.trash.telling", detail.TrashTellingCommand));
        menu.Items.Add(Item("concepts.trash.just", detail.TrashJustCommand));
        FlyoutBase.SetAttachedFlyout(button, menu);
        FlyoutBase.ShowAttachedFlyout(button);
        e.Handled = true;
    }

    private MenuItem Item(string key, ICommand command) => new()
    {
        Header = this.TryFindResource(key, out var text) ? text : key,
        Command = command,
    };
}
