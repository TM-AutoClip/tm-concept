using TMConcept.Core.DiskSpace;

namespace TMConcept.Desktop.DiskSpace;

/// <summary>
/// Which rows "Clean up unused" and the full delete take (by the rows' own flags; a used component is never cleaned up). Neither ever takes
/// a model (a voice, an image model, the meaning-check model, an Ollama model, the text recognition), a document of the user or a library
/// orphan row: those go only when their row is ticked ("Remove selected"). The one model row they do take is the app's Ollama leftovers
/// (the received part of a cancelled pull, files no model uses: not a usable model).
/// </summary>
public static class DiskSelection
{
    /// <summary>The key of the row of the app's Ollama files no model uses (<see cref="LeftoverDiskSource"/>).</summary>
    public const string AppModelLeftoversKey = "models:app-ollama-unused";

    /// <summary>"Clean up unused": unused tools and the safe leftovers; never a used one, never a model.</summary>
    public static IReadOnlyList<IDiskItem> Unused(IEnumerable<IDiskItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items.Where(i => i.Removable && i.InCleanup && i.State != DiskItemState.Used && i.UsedBy.Count == 0 && !Kept(i)).ToList();
    }

    /// <summary>The full delete: every tool and cache; never a model, a document or a library row.</summary>
    public static IReadOnlyList<IDiskItem> Full(IEnumerable<IDiskItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items.Where(i => i.Removable && i.InFullDelete && !Kept(i)).ToList();
    }

    /// <summary>Whether a row goes only when it is ticked (a model, a library row).</summary>
    public static bool Kept(IDiskItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return (item.Area == DiskArea.Models && item.Key != AppModelLeftoversKey) || item.Area == DiskArea.Library;
    }
}
