namespace TMConcept.Storage.Backup;

/// <summary>A sample image file inside a backup: its path relative to the library folder, its length in the payload and its SHA-256.</summary>
public sealed class BackupImage
{
    /// <summary>Path relative to the library folder (as the database stores it).</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Bytes in the payload.</summary>
    public long Length { get; set; }

    /// <summary>SHA-256 (hex, lower case).</summary>
    public string Sha256 { get; set; } = string.Empty;
}
