using Avalonia;
using Avalonia.Controls;
using TMConcept.Core.Markers;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Desktop.Views;

/// <summary>
/// A drop-down of markers next to a text box: the chosen marker is put into <see cref="Target"/> at its caret (replacing a selection), the
/// caret goes after it and the drop-down is empty again.
/// </summary>
public sealed partial class MarkerPicker : UserControl
{
    /// <summary>The text box the markers go into.</summary>
    public static readonly StyledProperty<TextBox?> TargetProperty = AvaloniaProperty.Register<MarkerPicker, TextBox?>(nameof(Target));

    /// <summary>The markers offered.</summary>
    public static readonly StyledProperty<IReadOnlyList<MarkerOption>?> MarkersProperty =
        AvaloniaProperty.Register<MarkerPicker, IReadOnlyList<MarkerOption>?>(nameof(Markers));

    /// <summary>Creates.</summary>
    public MarkerPicker()
    {
        InitializeComponent();
        List.SelectionChanged += (_, _) =>
        {
            if (List.SelectedItem is MarkerOption option)
            {
                Insert(option);
                List.SelectedItem = null;
            }
        };
    }

    /// <summary>The text box the markers go into.</summary>
    public TextBox? Target
    {
        get => GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    /// <summary>The markers offered.</summary>
    public IReadOnlyList<MarkerOption>? Markers
    {
        get => GetValue(MarkersProperty);
        set => SetValue(MarkersProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MarkersProperty)
        {
            List.ItemsSource = Markers;
        }
    }

    /// <summary>Puts the marker into the target at its caret (the text box's binding carries it to the view model).</summary>
    public void Insert(MarkerOption option)
    {
        ArgumentNullException.ThrowIfNull(option);
        if (Target is not { } box)
        {
            return;
        }

        // No selection: the caret (the text box keeps it while the drop-down has the focus).
        var (start, end) = box.SelectionStart == box.SelectionEnd ? (box.CaretIndex, box.CaretIndex) : (box.SelectionStart, box.SelectionEnd);
        var (text, caret) = MarkerText.Insert(box.Text, start, end, option.Token);

        box.Text = text;
        box.CaretIndex = caret;
        box.SelectionStart = box.SelectionEnd = caret;
        box.Focus();
    }
}
