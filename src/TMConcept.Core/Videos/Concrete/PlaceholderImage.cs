using System.Buffers.Binary;
using System.IO.Compression;

namespace TMConcept.Core.Videos;

/// <summary>
/// The stand-in picture of a scene in the plan preview (F10, 0.1.49): a grey PNG of the scene image's size with a lighter frame, written by the
/// app itself (no image model, nothing paid). The real image replaces it under the same name once the plan is approved.
/// </summary>
public static class PlaceholderImage
{
    private static readonly uint[] CrcTable = BuildTable();

    /// <summary>Writes a <paramref name="width"/> × <paramref name="height"/> placeholder to <paramref name="file"/>.</summary>
    public static async Task WriteAsync(string file, int width, int height, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(file);
        var bytes = Png(Math.Max(1, width), Math.Max(1, height));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        await File.WriteAllBytesAsync(file, bytes, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The PNG bytes (8-bit grey; every row filtered with "none").</summary>
    public static byte[] Png(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var edge = Math.Max(2, Math.Min(width, height) / 40);
        using var raw = new MemoryStream();
        using (var zlib = new ZLibStream(raw, CompressionLevel.Fastest, leaveOpen: true))
        {
            var row = new byte[width + 1];
            for (var y = 0; y < height; y++)
            {
                var band = y < edge || y >= height - edge;
                for (var x = 0; x < width; x++)
                {
                    row[x + 1] = band || x < edge || x >= width - edge ? PlanPreviewLimits.PlaceholderEdge : PlanPreviewLimits.PlaceholderGrey;
                }

                zlib.Write(row);
            }
        }

        using var png = new MemoryStream();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", raw.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void Chunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);
        var typed = new byte[4 + data.Length];
        for (var i = 0; i < 4; i++)
        {
            typed[i] = (byte)type[i];
        }

        data.CopyTo(typed, 4);
        stream.Write(typed);
        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc(typed));
        stream.Write(crc);
    }

    private static uint Crc(byte[] bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (var n = 0u; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
