using System.Text;

namespace TMConcept.Documents.Extractors;

/// <summary>
/// Reads a text file whose encoding is not declared: a byte order mark wins, then strict UTF-8, otherwise the Windows "ANSI" page of the
/// text (<see cref="LegacyEncodings.DecodeAnsi"/>: Turkish when its letters are there, else Western; never fails).
/// </summary>
public static class TextFiles
{
    /// <summary>The text of <paramref name="file"/>.</summary>
    public static string Read(string file) => Decode(File.ReadAllBytes(file));

    /// <summary>The text of <paramref name="bytes"/>.</summary>
    public static string Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        if (bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return LegacyEncodings.DecodeAnsi(bytes);
        }
    }
}
