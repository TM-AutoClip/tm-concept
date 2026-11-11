using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Desktop.Views;

/// <summary>
/// The folded models line of a page (0.1.13, the developer: the model choices "take needless room"): the choices of <see cref="Picker"/>
/// in one line; <see cref="IsOpen"/> tells the page to show the pickers.
/// </summary>
public sealed partial class ModelSummaryBar : UserControl
{
    /// <summary>The page's pickers.</summary>
    public static readonly StyledProperty<StepPickerViewModel?> PickerProperty = AvaloniaProperty.Register<ModelSummaryBar, StepPickerViewModel?>(nameof(Picker));

    /// <summary>Whether the pickers are unfolded.</summary>
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<ModelSummaryBar, bool>(nameof(IsOpen), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Creates.</summary>
    public ModelSummaryBar() => InitializeComponent();

    /// <summary>The page's pickers.</summary>
    public StepPickerViewModel? Picker
    {
        get => GetValue(PickerProperty);
        set => SetValue(PickerProperty, value);
    }

    /// <summary>Whether the pickers are unfolded.</summary>
    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }
}
