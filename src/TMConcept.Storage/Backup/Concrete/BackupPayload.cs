using System.Security.Cryptography;

namespace TMConcept.Storage.Backup;

/// <summary>The opened content of a backup: its header and the parts of the plain bytes (database key, database copy, image files).</summary>
internal sealed class BackupPayload(BackupHeader header, byte[] plain, int keyAt, int databaseAt, int imagesAt)
{
    /// <summary>The header (format 02 files get one made from what they always held).</summary>
    public BackupHeader Header { get; } = header;

    /// <summary>The database key (a copy; zeroed by <see cref="Clear"/> only in the plain bytes).</summary>
    public byte[] Key { get; } = plain.AsSpan(keyAt, 32).ToArray();

    /// <summary>The encrypted database copy.</summary>
    public ReadOnlyMemory<byte> Database => plain.AsMemory(databaseAt, imagesAt - databaseAt);

    /// <summary>The image files, one after the other (<see cref="BackupHeader.Images"/> gives the lengths).</summary>
    public ReadOnlySpan<byte> Images => plain.AsSpan(imagesAt);

    /// <summary>Zeroes the plain bytes and the key.</summary>
    public void Clear()
    {
        CryptographicOperations.ZeroMemory(plain);
        CryptographicOperations.ZeroMemory(Key);
    }
}
