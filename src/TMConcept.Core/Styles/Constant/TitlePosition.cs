namespace TMConcept.Core.Styles;

/// <summary>
/// Where the scene titles sit: a cell of a 3×3 grid, row by row from the top left (the value is row × 3 + column; stored as numbers).
/// <see cref="TitleAnchors"/> turns it into anchor fractions.
/// </summary>
public enum TitlePosition
{
    /// <summary>Top row, left.</summary>
    TopLeft = 0,

    /// <summary>Top row, centre.</summary>
    TopCenter = 1,

    /// <summary>Top row, right.</summary>
    TopRight = 2,

    /// <summary>Middle row, left.</summary>
    MiddleLeft = 3,

    /// <summary>Middle row, centre.</summary>
    MiddleCenter = 4,

    /// <summary>Middle row, right.</summary>
    MiddleRight = 5,

    /// <summary>Bottom row, left.</summary>
    BottomLeft = 6,

    /// <summary>Bottom row, centre.</summary>
    BottomCenter = 7,

    /// <summary>Bottom row, right.</summary>
    BottomRight = 8,
}
