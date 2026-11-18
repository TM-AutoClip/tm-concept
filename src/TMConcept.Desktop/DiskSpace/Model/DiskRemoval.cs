using TMConcept.Core.DiskSpace;

namespace TMConcept.Desktop.DiskSpace;

/// <summary>
/// How a removal ended: refused because work runs (<see cref="Busy"/>, nothing was touched), and otherwise what was deleted for good
/// (<see cref="Removed"/>) and what failed with its reason (<see cref="Failed"/>).
/// </summary>
public sealed record DiskRemoval(bool Busy, IReadOnlyList<IDiskItem> Removed, IReadOnlyList<(IDiskItem Item, string Reason)> Failed)
{
    /// <summary>Refused: work (a video, a generation, a narration, an install) is running.</summary>
    public static DiskRemoval Refused { get; } = new(true, [], []);

    /// <summary>Bytes taken away.</summary>
    public long Freed => Removed.Sum(i => i.Bytes);
}
