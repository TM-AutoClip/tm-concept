namespace TMConcept.Storage.Database;

/// <summary>
/// The database file was made by another version of TM Concept (an older one, or a newer one) and this version does not open it: older
/// files are not upgraded any more (no backward compatibility, 7 Nov 2026). Nothing in the file was changed. The app offers to set the
/// file aside (renamed, never deleted) and to start fresh.
/// </summary>
public sealed class DatabaseVersionException : InvalidOperationException
{
    /// <summary>Creates.</summary>
    public DatabaseVersionException()
    {
    }

    /// <summary>With a message.</summary>
    public DatabaseVersionException(string message)
        : base(message)
    {
    }

    /// <summary>With a message and an inner error.</summary>
    public DatabaseVersionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The schema version found in the file (0 = none written: a file of 0.1.0–0.1.6).</summary>
    public int Found { get; init; }

    /// <summary>Whether the file is newer than this app (updating the app opens it).</summary>
    public bool Newer => Found > ConceptSchema.Version;
}
