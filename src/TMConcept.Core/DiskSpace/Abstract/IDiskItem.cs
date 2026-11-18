namespace TMConcept.Core.DiskSpace;

/// <summary>
/// One row of the storage manager (Settings → Storage): an installed component, or space no component owns (leftovers, work caches, old
/// folders). A later version adds its own rows (documents, the app's Ollama models) through a new <see cref="IDiskItemSource"/>; they take
/// part in "Remove selected", "Clean up unused" and the full delete by their flags.
/// </summary>
public interface IDiskItem
{
    /// <summary>A key that is the same on the next listing ("component:voice.kokoro", "work:speech-cache").</summary>
    string Key { get; }

    /// <summary>Display name.</summary>
    string Name { get; }

    /// <summary>Where it lives (for the totals).</summary>
    DiskArea Area { get; }

    /// <summary>Used, unused, incomplete, or a leftover.</summary>
    DiskItemState State { get; }

    /// <summary>Bytes on disk.</summary>
    long Bytes { get; }

    /// <summary>Who needs it (empty when nothing does).</summary>
    IReadOnlyList<DiskUse> UsedBy { get; }

    /// <summary>The folders it takes away.</summary>
    IReadOnlyList<string> Paths { get; }

    /// <summary>Whether it may be selected and removed.</summary>
    bool Removable { get; }

    /// <summary>Whether "Clean up unused" takes it: never anything that is used; only what is safe to lose and comes back when needed.</summary>
    bool InCleanup { get; }

    /// <summary>
    /// Whether the full delete takes it (tools and caches; never the database, backups, logs, the videos, a document; a model is kept by
    /// <c>DiskSelection</c> whatever this says).
    /// </summary>
    bool InFullDelete { get; }

    /// <summary>Takes it away through <paramref name="discard"/> (the install record forgets a component). Throws when it could not.</summary>
    Task RemoveAsync(IFolderDiscard discard, CancellationToken cancellationToken);
}
