using TMConcept.Core.DiskSpace;

namespace TMConcept.Core.Documents;

/// <summary>
/// A document as a storage row (Settings → Storage): it is the user's own and in use by its profile, so neither "Clean up unused" nor the
/// full delete takes it (0.1.28); only "Remove selected" does, with its passages (<see cref="DocumentLibrary.DeleteAsync"/>: the copy is
/// deleted for good first). Removable
/// also when its copy is already gone (the record and passages still take room in the database).
/// </summary>
public sealed class DocumentDiskItem(SourceDocument document, string profileName, DocumentLibrary library, long bytes, string path)
    : DiskItemBase(KeyOf(document.Id), document.Title, DiskArea.Documents, DiskItemState.Used, bytes, [path])
{
    /// <summary>The document.</summary>
    public SourceDocument Document { get; } = document;

    /// <inheritdoc />
    public override IReadOnlyList<DiskUse> UsedBy { get; } = [new DiskUse(profileName, "documents")];

    /// <inheritdoc />
    public override bool Removable => true;

    /// <inheritdoc />
    public override bool InCleanup => false;

    /// <inheritdoc />
    public override bool InFullDelete => false;

    /// <summary>The key of a document's row.</summary>
    public static string KeyOf(Guid id) => "document:" + id.ToString("N");

    /// <inheritdoc />
    public override Task RemoveAsync(IFolderDiscard discard, CancellationToken cancellationToken) => library.DeleteAsync(Document, discard, cancellationToken);
}
