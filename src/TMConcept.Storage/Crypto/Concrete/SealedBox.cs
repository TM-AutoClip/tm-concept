using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace TMConcept.Storage.Crypto;

/// <summary>
/// Password sealing with standard parts only (no home-made cipher): Argon2id derives a 256-bit key from the password, AES-256-GCM encrypts.
/// Layout: magic (8) | version (1) | memory KiB (4, LE) | iterations (4, LE) | lanes (1) | salt (16) | nonce (12) | ciphertext | tag (16).
/// The whole header is GCM's additional data, so changing a parameter makes the file fail to open. A key derived once
/// (<see cref="Derive"/>) can seal many times without the password (daily backups); opening always needs the password. Used for the
/// master-password key file and for backups (different magic words).
/// </summary>
public static class SealedBox
{
    private const byte Version = 1;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int HeaderSize = 8 + 1 + 4 + 4 + 1 + SaltSize + NonceSize;

    /// <summary>Highest accepted memory cost when opening (1 GiB): a forged header cannot make the app allocate more.</summary>
    private const int MaxMemoryKiB = 1024 * 1024;

    /// <summary>Derives a key from a password with a new random salt.</summary>
    public static DerivedKey Derive(string password, KdfParameters kdf)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(kdf);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        return new DerivedKey(salt, kdf, DeriveKey(password, salt, kdf));
    }

    /// <summary>Seals <paramref name="plaintext"/> with a password.</summary>
    public static byte[] Seal(ReadOnlySpan<byte> magic, string password, ReadOnlySpan<byte> plaintext, KdfParameters kdf)
    {
        var key = Derive(password, kdf);
        try
        {
            return Seal(magic, key, plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key.Key);
        }
    }

    /// <summary>Seals <paramref name="plaintext"/> with a derived key (the password is needed to open).</summary>
    public static byte[] Seal(ReadOnlySpan<byte> magic, DerivedKey key, ReadOnlySpan<byte> plaintext)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (magic.Length != 8)
        {
            throw new ArgumentException("The magic word must be 8 bytes.", nameof(magic));
        }

        if (key.Salt.Length != SaltSize || key.Key.Length != 32)
        {
            throw new ArgumentException("The derived key is not valid.", nameof(key));
        }

        var output = new byte[HeaderSize + plaintext.Length + TagSize];
        var header = output.AsSpan(0, HeaderSize);
        magic.CopyTo(header);
        header[8] = Version;
        BinaryPrimitives.WriteInt32LittleEndian(header[9..], key.Kdf.MemoryKiB);
        BinaryPrimitives.WriteInt32LittleEndian(header[13..], key.Kdf.Iterations);
        header[17] = checked((byte)key.Kdf.Parallelism);
        key.Salt.CopyTo(header.Slice(18, SaltSize));
        RandomNumberGenerator.Fill(header.Slice(18 + SaltSize, NonceSize));
        using var aes = new AesGcm(key.Key, TagSize);
        aes.Encrypt(header.Slice(18 + SaltSize, NonceSize), plaintext, output.AsSpan(HeaderSize, plaintext.Length),
            output.AsSpan(HeaderSize + plaintext.Length, TagSize), header);
        return output;
    }

    /// <summary>Opens a sealed box; <see cref="SealedBoxException"/> for a wrong password, a damaged file or another file type.</summary>
    public static byte[] Open(ReadOnlySpan<byte> magic, string password, ReadOnlySpan<byte> sealedData)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (sealedData.Length < HeaderSize + TagSize || !sealedData[..8].SequenceEqual(magic) || sealedData[8] != Version)
        {
            throw new SealedBoxException("This is not a TM Concept file of the expected type, or it is damaged.");
        }

        var header = sealedData[..HeaderSize];
        var kdf = new KdfParameters(BinaryPrimitives.ReadInt32LittleEndian(header[9..]), BinaryPrimitives.ReadInt32LittleEndian(header[13..]), header[17]);
        if (kdf.MemoryKiB is < 1024 or > MaxMemoryKiB || kdf.Iterations is < 1 or > 64 || kdf.Parallelism is < 1 or > 16)
        {
            throw new SealedBoxException("The file has unsupported key derivation parameters; it may be damaged.");
        }

        var key = DeriveKey(password, header.Slice(18, SaltSize).ToArray(), kdf);
        var length = sealedData.Length - HeaderSize - TagSize;
        var plaintext = new byte[length];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(header.Slice(18 + SaltSize, NonceSize), sealedData.Slice(HeaderSize, length), sealedData.Slice(HeaderSize + length, TagSize),
                plaintext, header);
            return plaintext;
        }
        catch (AuthenticationTagMismatchException ex)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new SealedBoxException("Wrong password, or the file is damaged.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static byte[] DeriveKey(string password, byte[] salt, KdfParameters kdf)
    {
        var bytes = Encoding.UTF8.GetBytes(password.Normalize(NormalizationForm.FormC));
        try
        {
            using var argon = new Argon2id(bytes)
            {
                Salt = salt,
                MemorySize = kdf.MemoryKiB,
                Iterations = kdf.Iterations,
                DegreeOfParallelism = kdf.Parallelism,
            };
            return argon.GetBytes(32);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
