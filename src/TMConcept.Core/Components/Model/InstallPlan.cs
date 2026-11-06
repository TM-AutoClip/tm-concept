namespace TMConcept.Core.Components;

/// <summary>
/// What must be installed for a set of components: the missing ones in install order (dependencies first), their downloads, and the
/// ones this computer has no download for.
/// </summary>
public sealed record InstallPlan(IReadOnlyList<IComponent> Missing, IReadOnlyList<ComponentDownload> Downloads, IReadOnlyList<IComponent> Unsupported)
{
    /// <summary>Nothing to do.</summary>
    public bool Empty => Missing.Count == 0 && Unsupported.Count == 0;

    /// <summary>About how many bytes are downloaded.</summary>
    public long TotalBytes => Downloads.Sum(d => d.ApproximateBytes);
}
