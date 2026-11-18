using TMConcept.Core.DiskSpace;

namespace TMConcept.Core.Documents;

/// <summary>
/// Kept page texts of a PDF file that no document of the profile holds any more (0.1.25): an add that was canceled or stopped keeps what it
/// read so that adding the file again does not read (or pay for) those pages again. A leftover row of the storage manager: the clean-up may
/// take it; removing it deletes the kept texts from the database (<see cref="IDocumentPageStore.ForgetAsync"/>).
/// </summary>
public sealed class DocumentPagesDiskItem(Guid profileId, string fileSha256, string name, long bytes, IDocumentPageStore pages)
    : DiskItemBase(KeyOf(profileId, fileSha256), name, DiskArea.Documents, DiskItemState.Leftover, bytes, [])
{
    /// <inheritdoc />
    public override bool Removable => true;

    /// <inheritdoc />
    public override bool InCleanup => true;

    /// <inheritdoc />
    public override bool InFullDelete => true;

    /// <summary>The key of a file's kept pages.</summary>
    public static string KeyOf(Guid profileId, string fileSha256) => "document-pages:" + profileId.ToString("N") + ":" + fileSha256;

    /// <inheritdoc />
    public override Task RemoveAsync(IFolderDiscard discard, CancellationToken cancellationToken) => pages.ForgetAsync(profileId, fileSha256, cancellationToken);
}
