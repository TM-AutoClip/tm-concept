using Avalonia;
using Avalonia.Controls;

namespace TMConcept.Desktop.Controls;

/// <summary>
/// Two children side by side when the panel is at least <see cref="Breakpoint"/> wide (the first one takes <see cref="Ratio"/> of the
/// width), stacked when it is narrower (the first one takes <see cref="StackedRatio"/> of the height, half by default, or with
/// <see cref="FitFirst"/> only the height it needs up to that share; each at least its own <see cref="Layoutable.MinHeight"/>). Below
/// <see cref="WideBelowHeight"/> they stand side by side whatever the width: stacked, neither would have room. Avalonia has no width breakpoints; this one small panel replaces width checks in the
/// code-behind of every page that must stack (0.1.13: the produced videos go under the queue on a narrow window, and their list stands
/// beside the preview when that leaves the card wide). Exactly two children: another count is a mistake in the view and throws.
/// </summary>
public sealed class AdaptiveSplitPanel : Panel
{
    /// <summary>The narrowest width at which the children stand side by side.</summary>
    public static readonly StyledProperty<double> BreakpointProperty =
        AvaloniaProperty.Register<AdaptiveSplitPanel, double>(nameof(Breakpoint), 1200);

    /// <summary>The first child's share of the width when side by side (0–1).</summary>
    public static readonly StyledProperty<double> RatioProperty =
        AvaloniaProperty.Register<AdaptiveSplitPanel, double>(nameof(Ratio), 0.5, coerce: (_, value) => Math.Clamp(value, 0, 1));

    /// <summary>The first child's share of the height when stacked (0–1).</summary>
    public static readonly StyledProperty<double> StackedRatioProperty =
        AvaloniaProperty.Register<AdaptiveSplitPanel, double>(nameof(StackedRatio), 0.5, coerce: (_, value) => Math.Clamp(value, 0, 1));

    /// <summary>When stacked, the first child takes only the height it needs (at most <see cref="StackedRatio"/>); the second one the rest.</summary>
    public static readonly StyledProperty<bool> FitFirstProperty =
        AvaloniaProperty.Register<AdaptiveSplitPanel, bool>(nameof(FitFirst));

    /// <summary>Below this height the children stand side by side whatever the width (0 = never).</summary>
    public static readonly StyledProperty<double> WideBelowHeightProperty =
        AvaloniaProperty.Register<AdaptiveSplitPanel, double>(nameof(WideBelowHeight));

    /// <summary>The gap between the children, across or down.</summary>
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<AdaptiveSplitPanel, double>(nameof(Spacing), 0, coerce: (_, value) => Math.Max(0, value));

    static AdaptiveSplitPanel() => AffectsMeasure<AdaptiveSplitPanel>(BreakpointProperty, RatioProperty, StackedRatioProperty, FitFirstProperty,
        WideBelowHeightProperty, SpacingProperty);

    /// <summary>The narrowest width at which the children stand side by side.</summary>
    public double Breakpoint
    {
        get => GetValue(BreakpointProperty);
        set => SetValue(BreakpointProperty, value);
    }

    /// <summary>The first child's share of the width when side by side (0–1).</summary>
    public double Ratio
    {
        get => GetValue(RatioProperty);
        set => SetValue(RatioProperty, value);
    }

    /// <summary>The first child's share of the height when stacked (0–1).</summary>
    public double StackedRatio
    {
        get => GetValue(StackedRatioProperty);
        set => SetValue(StackedRatioProperty, value);
    }

    /// <summary>When stacked, the first child takes only the height it needs (at most <see cref="StackedRatio"/>); the second one the rest.</summary>
    public bool FitFirst
    {
        get => GetValue(FitFirstProperty);
        set => SetValue(FitFirstProperty, value);
    }

    /// <summary>Below this height the children stand side by side whatever the width (0 = never).</summary>
    public double WideBelowHeight
    {
        get => GetValue(WideBelowHeightProperty);
        set => SetValue(WideBelowHeightProperty, value);
    }

    /// <summary>The gap between the children, across or down.</summary>
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>Whether the children stand side by side at <paramref name="width"/> (an unbounded width counts as wide).</summary>
    public bool IsWideAt(double width) => IsWideAt(new Size(width, double.PositiveInfinity));

    /// <summary>Whether the children stand side by side in <paramref name="size"/> (wide enough, or too low to stack).</summary>
    public bool IsWideAt(Size size) => double.IsInfinity(size.Width) || size.Width >= Breakpoint || size.Height < WideBelowHeight;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var (first, second) = Pair();
        if (IsWideAt(availableSize))
        {
            if (double.IsInfinity(availableSize.Width))
            {
                first.Measure(availableSize);
                second.Measure(availableSize);
                return new Size(first.DesiredSize.Width + Spacing + second.DesiredSize.Width,
                    Math.Max(first.DesiredSize.Height, second.DesiredSize.Height));
            }

            var (left, right) = Widths(availableSize.Width);
            first.Measure(availableSize.WithWidth(left));
            second.Measure(availableSize.WithWidth(right));
            return new Size(availableSize.Width, Math.Max(first.DesiredSize.Height, second.DesiredSize.Height));
        }

        if (double.IsInfinity(availableSize.Height))
        {
            first.Measure(availableSize);
            second.Measure(availableSize);
            return new Size(Math.Max(first.DesiredSize.Width, second.DesiredSize.Width),
                first.DesiredSize.Height + Spacing + second.DesiredSize.Height);
        }

        var (top, bottom) = Heights(first, second, availableSize.Height, availableSize.Width);
        first.Measure(availableSize.WithHeight(top));
        second.Measure(availableSize.WithHeight(bottom));
        return new Size(Math.Max(first.DesiredSize.Width, second.DesiredSize.Width), top + Spacing + bottom);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var (first, second) = Pair();
        if (IsWideAt(finalSize))
        {
            var (left, right) = Widths(finalSize.Width);
            first.Arrange(new Rect(0, 0, left, finalSize.Height));
            second.Arrange(new Rect(left + Spacing, 0, right, finalSize.Height));
        }
        else
        {
            var (top, bottom) = Heights(first, second, finalSize.Height, finalSize.Width);
            first.Arrange(new Rect(0, 0, finalSize.Width, top));
            second.Arrange(new Rect(0, top + Spacing, finalSize.Width, bottom));
        }

        return finalSize;
    }

    private (Control First, Control Second) Pair() => Children.Count == 2
        ? (Children[0], Children[1])
        : throw new InvalidOperationException($"{nameof(AdaptiveSplitPanel)} takes exactly 2 children, not {Children.Count}.");

    private (double Left, double Right) Widths(double width)
    {
        var free = Math.Max(0, width - Spacing);
        var left = free * Ratio;
        return (left, free - left);
    }

    private (double Top, double Bottom) Heights(Control first, Control second, double height, double width)
    {
        var free = Math.Max(0, height - Spacing);
        var top = free * StackedRatio;
        if (FitFirst)
        {
            // What the first child wants within its share (a short list leaves the rest to the second one).
            first.Measure(new Size(width, top));
            top = Math.Min(top, first.DesiredSize.Height);
        }

        top = Math.Max(first.MinHeight, top);
        return (top, Math.Max(second.MinHeight, free - top));
    }
}
