namespace TMConcept.Core.Videos;

/// <summary>
/// What the motion check needs of every frame of a video (<see cref="MotionFrameReader"/>), in order: its mean brightness, its mean change
/// from the frame before (0 for the first) and the share of the grid's cells that changed a lot (all 0–1), and the frame rate.
/// </summary>
public sealed class MotionSeries(double fps, IReadOnlyList<double> luminance, IReadOnlyList<double> change, IReadOnlyList<double> cutShare)
{
    /// <summary>Frames per second.</summary>
    public double Fps { get; } = fps > 0 ? fps : 30;

    /// <summary>Mean brightness of each frame.</summary>
    public IReadOnlyList<double> Luminance { get; } = luminance;

    /// <summary>Mean change of each frame from the one before.</summary>
    public IReadOnlyList<double> Change { get; } = change;

    /// <summary>Share of the cells of each frame that changed a lot from the one before.</summary>
    public IReadOnlyList<double> CutShare { get; } = cutShare;

    /// <summary>Frames read.</summary>
    public int Count => Luminance.Count;

    /// <summary>The time of frame <paramref name="index"/>, seconds.</summary>
    public double Time(int index) => index / Fps;
}
