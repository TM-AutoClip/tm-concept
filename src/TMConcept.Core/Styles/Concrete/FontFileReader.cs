using System.Buffers.Binary;
using System.Text;

namespace TMConcept.Core.Styles;

/// <summary>
/// Reads the family, weight, slant and script coverage of a TrueType / OpenType file from its <c>name</c> and <c>OS/2</c> tables (only the
/// table directory and those two tables are read, a few kilobytes). Collections (.ttc) and other files are not fonts here.
/// </summary>
public static class FontFileReader
{
    private const int Typographic = 16;
    private const int Family = 1;

    /// <summary>The file's details; <c>null</c> for a file that is not a single TrueType / OpenType font or misses the tables.</summary>
    public static FontFileInfo? Read(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        try
        {
            using var stream = File.OpenRead(path);
            return Read(stream, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EndOfStreamException)
        {
            // A font file that cannot be read is not offered (the caller lists only what it can use).
            return null;
        }
    }

    /// <summary>The details of a font read from <paramref name="stream"/>.</summary>
    public static FontFileInfo? Read(Stream stream, string path)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Span<byte> head = stackalloc byte[12];
        if (stream.Read(head) != 12)
        {
            return null;
        }

        var version = BinaryPrimitives.ReadUInt32BigEndian(head);
        if (version is not (0x00010000 or 0x4F54544F or 0x74727565))
        {
            // Not "\0\1\0\0", "OTTO" or "true": a collection, a web font or not a font.
            return null;
        }

        var count = BinaryPrimitives.ReadUInt16BigEndian(head[4..]);
        var directory = new byte[count * 16];
        stream.ReadExactly(directory);
        (long Offset, int Length)? name = null, os2 = null;
        for (var i = 0; i < count; i++)
        {
            var entry = directory.AsSpan(i * 16, 16);
            var tag = Encoding.ASCII.GetString(entry[..4]);
            var table = ((long)BinaryPrimitives.ReadUInt32BigEndian(entry[8..]), (int)BinaryPrimitives.ReadUInt32BigEndian(entry[12..]));
            if (tag == "name")
            {
                name = table;
            }
            else if (tag == "OS/2")
            {
                os2 = table;
            }
        }

        if (name is not { } n || os2 is not { } o || n.Length is <= 6 or > 1_000_000 || o.Length < 46)
        {
            return null;
        }

        var family = FamilyName(Table(stream, n));
        if (string.IsNullOrWhiteSpace(family))
        {
            return null;
        }

        var metrics = Table(stream, (o.Offset, Math.Min(o.Length, 64)));
        var weight = BinaryPrimitives.ReadUInt16BigEndian(metrics.AsSpan(4));
        var range = BinaryPrimitives.ReadUInt32BigEndian(metrics.AsSpan(42));
        var selection = metrics.Length >= 64 ? BinaryPrimitives.ReadUInt16BigEndian(metrics.AsSpan(62)) : 0;
        return new FontFileInfo(path, family.Trim(), weight, (selection & 0x0201) != 0, (range & (1u << 2)) != 0, (range & (1u << 9)) != 0);
    }

    private static byte[] Table(Stream stream, (long Offset, int Length) table)
    {
        stream.Seek(table.Offset, SeekOrigin.Begin);
        var bytes = new byte[table.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    /// <summary>
    /// The family from the name table: the typographic family (16) before the family (1), a Windows Unicode record (platform 3) in US
    /// English before any other Windows one, then a Unicode-platform record (0).
    /// </summary>
    private static string? FamilyName(byte[] table)
    {
        var count = BinaryPrimitives.ReadUInt16BigEndian(table.AsSpan(2));
        var strings = BinaryPrimitives.ReadUInt16BigEndian(table.AsSpan(4));
        string? best = null;
        var bestRank = int.MaxValue;
        for (var i = 0; i < count && 6 + ((i + 1) * 12) <= table.Length; i++)
        {
            var record = table.AsSpan(6 + (i * 12), 12);
            var platform = BinaryPrimitives.ReadUInt16BigEndian(record);
            var language = BinaryPrimitives.ReadUInt16BigEndian(record[4..]);
            var id = BinaryPrimitives.ReadUInt16BigEndian(record[6..]);
            var length = BinaryPrimitives.ReadUInt16BigEndian(record[8..]);
            var offset = strings + BinaryPrimitives.ReadUInt16BigEndian(record[10..]);
            if (id is not (Typographic or Family) || platform is not (0 or 3) || offset + length > table.Length)
            {
                continue;
            }

            var rank = (id == Typographic ? 0 : 10) + (platform == 3 ? (language == 0x0409 ? 0 : 1) : 2);
            if (rank < bestRank)
            {
                bestRank = rank;
                best = Encoding.BigEndianUnicode.GetString(table, offset, length);
            }
        }

        return best;
    }
}
