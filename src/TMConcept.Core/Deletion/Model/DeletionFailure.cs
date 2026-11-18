namespace TMConcept.Core.Deletion;

/// <summary>A file or folder a delete could not remove, and why (shown to the user; the record that owns it stays).</summary>
public sealed record DeletionFailure(string Path, string Reason);
