using System.Security.Cryptography;

namespace TMConcept.Storage.Keys;

/// <summary>The open database key, in memory only.</summary>
public sealed class DatabaseKey
{
    private byte[]? _key;

    /// <summary>Whether a key is loaded.</summary>
    public bool IsLoaded => _key is not null;

    /// <summary>Loads (copies) the key.</summary>
    public void Load(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != 32)
        {
            throw new CryptographicException("The database key must be 32 bytes.");
        }

        Clear();
        _key = (byte[])key.Clone();
    }

    /// <summary>A copy of the key.</summary>
    public byte[] Copy() => (byte[])(_key ?? throw new InvalidOperationException("The database is locked.")).Clone();

    /// <summary>The key as the database passphrase (hex).</summary>
    public string Passphrase() => Convert.ToHexString(_key ?? throw new InvalidOperationException("The database is locked."));

    /// <summary>Forgets the key.</summary>
    public void Clear()
    {
        if (_key is not null)
        {
            CryptographicOperations.ZeroMemory(_key);
            _key = null;
        }
    }
}
