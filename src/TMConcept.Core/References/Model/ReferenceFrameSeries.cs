namespace TMConcept.Core.References;

/// <summary>
/// The numbers of every grey frame of a reference (<see cref="ReferenceFrameReader"/>), in order: the mean change from the frame before (0–1),
/// the share of the grid's cells that changed a lot, the correlation of the two grey histograms, the camera shift (pixels on the 160 × 96
/// frame; + = the camera moved right / down) and whether the shift explains the change. The first frame has no change, correlation 1 and no
/// shift.
/// </summary>
public sealed record ReferenceFrameSeries(double Fps, IReadOnlyList<double> Change, IReadOnlyList<double> CutShare, IReadOnlyList<double> Correlation,
    IReadOnlyList<double> ShiftX, IReadOnlyList<double> ShiftY, IReadOnlyList<bool> Moved)
{
    /// <summary>Frames read.</summary>
    public int Count => Change.Count;

    /// <summary>Seconds read.</summary>
    public double Seconds => Fps > 0 ? Count / Fps : 0;
}
