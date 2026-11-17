namespace TMConcept.Core.Documents;

/// <summary>Whether a document can be used (stored as its number).</summary>
public enum DocumentStatus
{
    /// <summary>Its text is in the database and can be searched.</summary>
    Ready = 0,

    /// <summary>No text could be read from it (a scanned PDF without a text layer, an empty file); it is kept so the user sees why.</summary>
    NoText = 1,
}
