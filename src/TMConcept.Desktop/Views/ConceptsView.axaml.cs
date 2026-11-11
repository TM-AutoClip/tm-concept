using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Desktop.Views;

/// <summary>
/// The concepts page: only a layout of its parts (the bar, the two sections, the list, the detail panel). Keys on the list: A approves a
/// new concept (takes an approved one back), D or Delete deletes it without a reason, ↑↓ move. While the detail panel edits a concept
/// (by hand or with AI) the generate sections fold away, so the editor gets the height of the page; they come back when it closes.
/// </summary>
public sealed partial class ConceptsView : UserControl
{
    private static readonly GridLength SectionsHeight = new(4, GridUnitType.Star);
    private ConceptDetailViewModel? _detail;

    /// <summary>Creates.</summary>
    public ConceptsView() => InitializeComponent();

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_detail is not null)
        {
            _detail.PropertyChanged -= OnDetailChanged;
        }

        _detail = (DataContext as ConceptsViewModel)?.Detail;
        if (_detail is not null)
        {
            _detail.PropertyChanged += OnDetailChanged;
        }

        FoldSections();
    }

    private void OnDetailChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConceptDetailViewModel.IsEditing) or nameof(ConceptDetailViewModel.IsAiEditing))
        {
            FoldSections();
        }
    }

    private void FoldSections()
    {
        var writing = _detail is { IsEditing: true } or { IsAiEditing: true };
        Sections.IsVisible = !writing;
        PageGrid.RowDefinitions[1].Height = writing ? new GridLength(0) : SectionsHeight;
    }

    private void OnBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ConceptsViewModel page)
        {
            page.Settings.ClosePanel();
            e.Handled = true;
        }
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ConceptsViewModel page || sender is not ListBox { SelectedItem: ConceptCardViewModel card })
        {
            return;
        }

        if (e.Key == Key.A)
        {
            (page.ShowApproved ? page.UnapproveRowCommand : page.ApproveRowCommand).Execute(card);
            e.Handled = true;
        }
        else if (e.Key is Key.D or Key.Delete)
        {
            page.TrashRowCommand.Execute(card);
            e.Handled = true;
        }
    }
}
