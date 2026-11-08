using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;

namespace TMConcept.Desktop.Views;

/// <summary>
/// The shared "?" help mark: a small round sign next to a title whose <see cref="Text"/> shows as a tooltip after a short delay on hover,
/// at once when it gets the keyboard focus (Tab), and is read out by screen readers. Long explanations live here instead of a line under
/// the title that is always open.
/// </summary>
public sealed partial class HelpTip : UserControl
{
    /// <summary>How long the pointer rests on the sign before the tip shows (milliseconds).</summary>
    public const int ShowDelay = 150;

    /// <summary>The explanation.</summary>
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<HelpTip, string?>(nameof(Text));

    /// <summary>Creates.</summary>
    public HelpTip()
    {
        InitializeComponent();
        ToolTip.SetShowDelay(this, ShowDelay);
    }

    /// <summary>The explanation.</summary>
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
        {
            ToolTip.SetTip(this, Text);
            AutomationProperties.SetHelpText(this, Text);
        }
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        // A "?" in a folding title (an expander's header) shows its tip and does not fold the area; a click opens the tip at once.
        base.OnPointerPressed(e);
        if (!string.IsNullOrEmpty(Text))
        {
            ToolTip.SetIsOpen(this, true);
        }

        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        if (!string.IsNullOrEmpty(Text))
        {
            ToolTip.SetIsOpen(this, true);
        }
    }

    /// <inheritdoc />
    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        ToolTip.SetIsOpen(this, false);
    }
}
