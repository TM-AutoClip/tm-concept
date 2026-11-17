using TMConcept.Core.Components;
using TMConcept.Core.DiskSpace;

namespace TMConcept.Desktop.DiskSpace;

/// <summary>
/// The storage manager (Settings → Storage): what takes space (<see cref="ReportAsync"/>) and taking it away safely
/// (<see cref="RemoveAsync"/>): never while work runs; the speech and Python workers are ended and the graphics card is freed first; to the
/// recycle bin, for good only after its own confirmation; the install record forgets removed components.
/// </summary>
public interface IDiskCleaner
{
    /// <summary>Every row of every <see cref="IDiskItemSource"/> with who needs it, and the totals by area.</summary>
    Task<DiskReport> ReportAsync(CancellationToken cancellationToken);

    /// <summary>Takes <paramref name="items"/> away (only removable ones; see the interface).</summary>
    Task<DiskRemoval> RemoveAsync(IReadOnlyList<IDiskItem> items, CancellationToken cancellationToken);

    /// <summary>One component as a row (the Remove button of Settings → General goes the same way).</summary>
    IDiskItem ItemOf(IComponent component);
}
