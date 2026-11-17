using TMConcept.Core.DiskSpace;

namespace TMConcept.Desktop.DiskSpace;

/// <summary>The bytes one area of the data folder takes (Settings → Storage, the totals line).</summary>
public sealed record DiskAreaTotal(DiskArea Area, long Bytes);
