using TMConcept.Core.DiskSpace;

namespace TMConcept.Desktop.DiskSpace;

/// <summary>Which rows "Clean up unused" and the full delete take (by the rows' own flags; a used component is never cleaned up).</summary>
public static class DiskSelection
{
    /// <summary>"Clean up unused": unused components and the safe leftovers; never a used one.</summary>
    public static IReadOnlyList<IDiskItem> Unused(IEnumerable<IDiskItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items.Where(i => i.Removable && i.InCleanup && i.State != DiskItemState.Used && i.UsedBy.Count == 0).ToList();
    }

    /// <summary>The full delete: every component and cache (later also documents and the app's Ollama models).</summary>
    public static IReadOnlyList<IDiskItem> Full(IEnumerable<IDiskItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items.Where(i => i.Removable && i.InFullDelete).ToList();
    }
}
