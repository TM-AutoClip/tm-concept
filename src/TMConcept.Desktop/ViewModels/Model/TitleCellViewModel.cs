using CommunityToolkit.Mvvm.ComponentModel;
using TMConcept.Core.Styles;

namespace TMConcept.Desktop.ViewModels;

/// <summary>One cell of the 3×3 title position grid of the template editor.</summary>
public sealed partial class TitleCellViewModel(TitlePosition position, string label) : ObservableObject
{
    /// <summary>The position.</summary>
    public TitlePosition Position { get; } = position;

    /// <summary>Its name ("Top left"), the tooltip of the cell.</summary>
    public string Label { get; } = label;

    /// <summary>The chosen cell.</summary>
    [ObservableProperty]
    public partial bool IsChecked { get; set; }
}
