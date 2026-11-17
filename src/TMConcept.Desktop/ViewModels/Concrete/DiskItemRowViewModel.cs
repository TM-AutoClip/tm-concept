using CommunityToolkit.Mvvm.ComponentModel;
using TMConcept.Core.DiskSpace;

namespace TMConcept.Desktop.ViewModels;

/// <summary>A row of Settings → Storage: a tick box, the name, its state, size and who uses it.</summary>
public sealed partial class DiskItemRowViewModel(IDiskItem item, string state, string size, string usedBy) : ObservableObject
{
    /// <summary>The row.</summary>
    public IDiskItem Item { get; } = item;

    /// <summary>Name.</summary>
    public string Name => Item.Name;

    /// <summary>"used", "unused", "incomplete", "can be cleaned".</summary>
    public string State { get; } = state;

    /// <summary>Whether it is used (green), incomplete (yellow) or neither.</summary>
    public bool IsUsed => Item.State == DiskItemState.Used;

    /// <summary>Incomplete (yellow).</summary>
    public bool IsIncomplete => Item.State == DiskItemState.Incomplete;

    /// <summary>Unused or a leftover (muted).</summary>
    public bool IsFree => !IsUsed && !IsIncomplete;

    /// <summary>"1.2 GB".</summary>
    public string Size { get; } = size;

    /// <summary>"Used by: Ottoman history / Narration" (empty when nothing uses it).</summary>
    public string UsedBy { get; } = usedBy;

    /// <summary>Whether the used-by line is shown.</summary>
    public bool HasUsedBy => UsedBy.Length > 0;

    /// <summary>Whether it can be ticked.</summary>
    public bool CanSelect => Item.Removable;

    /// <summary>Ticked for "Remove selected".</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
