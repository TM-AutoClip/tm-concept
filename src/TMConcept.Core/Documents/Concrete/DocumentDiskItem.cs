using TMConcept.Core.DiskSpace;

namespace TMConcept.Core.Documents;

/// <summary>
/// A document as a storage row (Settings → Storage): it is in use by its profile, so "Clean up unused" never takes it; the full delete and
/// "Remove selected" take it with its passages (<see cref="DocumentLibrary.DeleteAsync"/>: the copy to the recycle bin first). Removable
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
    public override bool InFullDelete => true;

    /// <summary>The key of a document's row.</summary>
    public static string KeyOf(Guid id) => "document:" + id.ToString("N");

    /// <inheritdoc />
    public override Task RemoveAsync(IFolderDiscard discard, CancellationToken cancellationToken) => library.DeleteAsync(Document, discard, cancellationToken);
}
