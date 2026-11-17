namespace TMConcept.Core.Styles;

/// <summary>Turns a <see cref="TitlePosition"/> into grid cell and anchor fractions, and back (the 3×3 picker and the render step).</summary>
public static class TitleAnchors
{
    /// <summary>Anchor of a position; throws <see cref="ArgumentOutOfRangeException"/> for a value outside the grid.</summary>
    public static TitleAnchor Of(TitlePosition position)
    {
        var value = (int)position;
        ArgumentOutOfRangeException.ThrowIfNegative(value, nameof(position));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 8, nameof(position));
        var row = value / 3;
        var column = value % 3;
        return new TitleAnchor(column / 2.0, row / 2.0, row, column);
    }

    /// <summary>The position of a grid cell (row and column 0–2).</summary>
    public static TitlePosition At(int row, int column)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(row, 2);
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(column, 2);
        return (TitlePosition)(row * 3 + column);
    }
}
