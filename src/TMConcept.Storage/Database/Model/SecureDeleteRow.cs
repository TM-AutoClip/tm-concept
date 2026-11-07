namespace TMConcept.Storage.Database;

/// <summary>The one value of <c>pragma_secure_delete</c> (read only): 1 when deleted content is overwritten with zeros (SQLite3 Multiple Ciphers is built with it on).</summary>
internal sealed class SecureDeleteRow
{
    /// <summary>Value.</summary>
    public long Value { get; set; }
}
