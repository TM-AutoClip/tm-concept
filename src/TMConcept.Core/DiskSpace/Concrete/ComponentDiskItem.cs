using TMConcept.Core.Components;

namespace TMConcept.Core.DiskSpace;

/// <summary>
/// An installed component as a storage row: its folders, size and state; who needs it. Removing it goes through
/// <see cref="ComponentPlanner.RemoveAsync"/> (one install or removal at a time; the install record forgets it). Clean-up takes it only
/// when nothing needs it; the full delete takes every component.
/// </summary>
public sealed class ComponentDiskItem(IComponent component, ComponentPlanner planner, long bytes, DiskItemState state, IReadOnlyList<DiskUse> usedBy)
    : DiskItemBase(KeyOf(component.Code), component.Name, AreaOf(component.Code), state, bytes, component.Folders())
{
    /// <summary>The component.</summary>
    public IComponent Component { get; } = component;

    /// <inheritdoc />
    public override IReadOnlyList<DiskUse> UsedBy { get; } = usedBy;

    /// <inheritdoc />
    public override bool InCleanup => Removable && UsedBy.Count == 0;

    /// <inheritdoc />
    public override bool InFullDelete => Removable;

    /// <summary>The key of a component's row.</summary>
    public static string KeyOf(string code) => "component:" + code;

    /// <summary>Models (voices, image models, the meaning-check model, the app's Ollama models) or tools (everything else).</summary>
    public static DiskArea AreaOf(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return code.StartsWith(ComponentCodes.VoicePrefix, StringComparison.Ordinal) || code.StartsWith(ComponentCodes.ImagePrefix, StringComparison.Ordinal)
            || code.StartsWith(ComponentCodes.EmbeddingPrefix, StringComparison.Ordinal)
            || code.StartsWith(ComponentCodes.AppOllamaModelPrefix, StringComparison.Ordinal)
            ? DiskArea.Models
            : DiskArea.Tools;
    }

    /// <inheritdoc />
    public override Task RemoveAsync(IFolderDiscard discard, CancellationToken cancellationToken) => planner.RemoveAsync(Component, discard, cancellationToken);
}
