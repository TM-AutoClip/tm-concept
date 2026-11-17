using TMConcept.Core.DiskSpace;

namespace TMConcept.Desktop.DiskSpace;

/// <summary>What the storage manager shows: every row (components first, then leftovers) and the totals by area.</summary>
public sealed record DiskReport(IReadOnlyList<IDiskItem> Items, IReadOnlyList<DiskAreaTotal> Totals);
