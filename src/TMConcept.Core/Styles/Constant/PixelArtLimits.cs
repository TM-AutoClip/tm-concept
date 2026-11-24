namespace TMConcept.Core.Styles;

/// <summary>
/// The numbers of the pixel art style (F13, 0.1.51, <see cref="DrawStyle.PixelArt"/>; read from ReelMimic's own pixel-art engine,
/// <c>pixel-art/template/src/pixel.js</c>, MIT, THIRD-PARTY-NOTICES.md; the drawer and the ramps are written for TM Concept): the scene is
/// drawn on a small buffer and blown up without smoothing, colours come from ramps of the palette, shading is ordered dithering, and
/// everything moves in steps. <c>plan.js</c> reads them from the input (<see cref="Render.RenderPixel"/>), so they live only here.
/// </summary>
public static class PixelArtLimits
{
    /// <summary>The buffer's long side in art pixels (480 × 270 across, 270 × 480 upright).</summary>
    public const int LongSide = 480;

    /// <summary>The buffer's short side in art pixels.</summary>
    public const int ShortSide = 270;

    /// <summary>How many times an art pixel is blown up on a 1080p frame (nearest neighbour: one art pixel = 4 × 4 screen pixels).</summary>
    public const int Scale = 4;

    /// <summary>The steps a second the pixel layer and the pixel transitions change in (stop-motion on twos at 24 frames).</summary>
    public const int Steps = 12;

    /// <summary>Colours of one ramp, dark to light (two darker steps, the palette's colour, two lighter steps).</summary>
    public const int RampLength = 5;

    /// <summary>The ordered dither matrix's side for shading (4 × 4 Bayer).</summary>
    public const int ShadeMatrix = 4;

    /// <summary>The ordered dither matrix's side for the dissolve (8 × 8 Bayer: 64 levels).</summary>
    public const int DissolveMatrix = 8;

    /// <summary>The height of one row of the blocks transition, in art pixels.</summary>
    public const int BlockSize = 16;

    /// <summary>
    /// The most black frames a transition may leave in a row: three or more read as a blink (ReelMimic's pixel-art rule). The pixel
    /// transitions never show black at all: the new scene is uncovered over the old one, so the motion check (<see cref="Videos.MotionCheck"/>,
    /// which reports any black frame inside the film) has nothing to report.
    /// </summary>
    public const int MaxDarkFrames = 2;
}
