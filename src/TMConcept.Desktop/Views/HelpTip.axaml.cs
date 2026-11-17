using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;

namespace TMConcept.Desktop.Views;

/// <summary>
/// The shared "?" help mark (0.1.23: on every page, to the right of a feature, a setting or a section). <see cref="Text"/> is the whole
/// explanation (two to four short sentences: what it does and how to use it). Hovering, or the keyboard focus (Tab), shows a short tooltip:
/// <see cref="Summary"/>, or the first sentence of the text when no summary is given. A click, Enter or Space opens the whole text in a
/// small panel next to the sign (a flyout; a click elsewhere or Escape closes it). Screen readers read the name "Help" and the whole text.
/// A "?" inside a folding title or a list entry never folds the area or selects the entry.
/// </summary>
public sealed partial class HelpTip : UserControl
{
    /// <summary>How long the pointer rests on the sign before the tip shows (milliseconds).</summary>
    public const int ShowDelay = 150;

    /// <summary>The widest the panel with the whole text gets (pixels): narrow enough for a 1024×600 window.</summary>
    public const double PanelWidth = 320;

    /// <summary>The whole explanation (the panel; also the tooltip's source when there is no <see cref="Summary"/>).</summary>
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<HelpTip, string?>(nameof(Text));

    /// <summary>The short tooltip (null = the first sentence of <see cref="Text"/>).</summary>
    public static readonly StyledProperty<string?> SummaryProperty = AvaloniaProperty.Register<HelpTip, string?>(nameof(Summary));

    private Flyout? _panel;
    private SelectableTextBlock? _panelText;

    /// <summary>Creates.</summary>
    public HelpTip()
    {
        InitializeComponent();
        ToolTip.SetShowDelay(this, ShowDelay);
    }

    /// <summary>The whole explanation.</summary>
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>The short tooltip (null = the first sentence of <see cref="Text"/>).</summary>
    public string? Summary
    {
        get => GetValue(SummaryProperty);
        set => SetValue(SummaryProperty, value);
    }

    /// <summary>What the tooltip shows.</summary>
    public string? Tip => !string.IsNullOrWhiteSpace(Summary) ? Summary : FirstSentence(Text);

    /// <summary>Whether the panel with the whole text is open.</summary>
    public bool IsPanelOpen => _panel?.IsOpen == true;

    /// <summary>The text the open panel shows (empty when it was never opened).</summary>
    public string PanelText => _panelText?.Text ?? string.Empty;

    /// <summary>
    /// The first sentence of <paramref name="text"/>: up to the first ".", "!", "?" or "…" followed by a space and a capital letter, a quote or
    /// a bracket (so "ör. ffmpeg", "e.g. a" and "0.1.17" do not end it); the whole text when it is one sentence.
    /// </summary>
    public static string FirstSentence(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var trimmed = text.Trim();
        var end = SentenceEnd().Match(trimmed);
        return end.Success ? trimmed[..end.Index].TrimEnd() : trimmed;
    }

    /// <summary>Opens the panel with the whole text (nothing when there is no text).</summary>
    public void OpenPanel()
    {
        if (string.IsNullOrWhiteSpace(Text))
        {
            return;
        }

        ToolTip.SetIsOpen(this, false);
        if (_panel is null)
        {
            _panelText = new SelectableTextBlock { Name = "HelpPanelText", TextWrapping = TextWrapping.Wrap, MaxWidth = PanelWidth, FontSize = 13 };
            _panel = new Flyout
            {
                Content = _panelText,
                Placement = PlacementMode.BottomEdgeAlignedLeft,
                ShowMode = FlyoutShowMode.Standard,
            };
        }

        _panelText!.Text = Text;
        _panel.ShowAt(this);
    }

    /// <summary>Closes the panel.</summary>
    public void ClosePanel() => _panel?.Hide();

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty || change.Property == SummaryProperty)
        {
            ToolTip.SetTip(this, string.IsNullOrEmpty(Tip) ? null : Tip);
            AutomationProperties.SetHelpText(this, Text);
            if (_panelText is not null && _panel?.IsOpen == true)
            {
                _panelText.Text = Text;
            }
        }
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        // A "?" in a folding title (an expander's header) or in a list entry opens its panel and neither folds the area nor picks the entry.
        base.OnPointerPressed(e);
        if (IsPanelOpen)
        {
            ClosePanel();
        }
        else
        {
            OpenPanel();
        }

        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Enter or Key.Space)
        {
            OpenPanel();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && IsPanelOpen)
        {
            ClosePanel();
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        if (!string.IsNullOrEmpty(Tip) && !IsPanelOpen)
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

    [GeneratedRegex(@"(?<=[.!?…])\s+(?=[\p{Lu}""“«(\[])")]
    private static partial Regex SentenceEnd();
}
