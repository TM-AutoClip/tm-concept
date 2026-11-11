using Avalonia;
using Avalonia.Controls;

namespace TMConcept.Desktop.Controls;

/// <summary>
/// Gives its child a height that follows its width (<see cref="Ratio"/> = height ÷ width; 16:9 = 0.5625), never more than the height it is
/// offered. The player's picture uses it, so the player fits its column on any window instead of a fixed height (0.1.12 drew it 400 px tall).
/// </summary>
public sealed class AspectBox : Decorator
{
    /// <summary>Height ÷ width.</summary>
    public static readonly StyledProperty<double> RatioProperty =
        AvaloniaProperty.Register<AspectBox, double>(nameof(Ratio), 9.0 / 16.0, coerce: (_, value) => Math.Max(0.01, value));

    static AspectBox() => AffectsMeasure<AspectBox>(RatioProperty);

    /// <summary>Height ÷ width.</summary>
    public double Ratio
    {
        get => GetValue(RatioProperty);
        set => SetValue(RatioProperty, value);
    }

    /// <summary>The size for <paramref name="width"/> within <paramref name="height"/> (an unbounded width keeps the child's own size).</summary>
    public Size SizeFor(double width, double height) =>
        double.IsInfinity(width) ? new Size(0, 0) : new Size(width, Math.Min(height, width * Ratio));

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (double.IsInfinity(availableSize.Width))
        {
            Child?.Measure(availableSize);
            return Child?.DesiredSize ?? default;
        }

        var size = SizeFor(availableSize.Width, availableSize.Height);
        Child?.Measure(size);
        return size;
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = SizeFor(finalSize.Width, finalSize.Height);
        Child?.Arrange(new Rect(0, 0, size.Width, size.Height));
        return finalSize;
    }
}
