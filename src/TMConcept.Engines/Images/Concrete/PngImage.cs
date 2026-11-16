using System.Buffers.Binary;
using System.IO.Compression;

namespace TMConcept.Engines.Images;

/// <summary>
/// A small PNG reader for one question: is the picture a single colour? (sd-cli writes 8-bit RGB PNGs; a blank black or white picture is a
/// known Vulkan result with some quantized weights.) Reads the IHDR and IDAT chunks, inflates them and undoes the row filters. Answers false
/// for what it does not read (another bit depth, a palette, interlacing, a broken file): such a picture is never called blank.
/// </summary>
public static class PngImage
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Whether the PNG file is one colour from corner to corner (false when it cannot be read).</summary>
    public static bool IsSingleColour(string file)
    {
        try
        {
            return IsSingleColour(File.ReadAllBytes(file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Whether the PNG bytes are one colour from corner to corner (false when they cannot be read).</summary>
    public static bool IsSingleColour(ReadOnlySpan<byte> png)
    {
        if (png.Length < 33 || !png[..8].SequenceEqual(Signature))
        {
            return false;
        }

        int width = 0, height = 0, channels = 0;
        using var data = new MemoryStream();
        var position = 8;
        while (position + 12 <= png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png[position..]);
            if (length < 0 || position + 12 + length > png.Length)
            {
                return false;
            }

            var type = png.Slice(position + 4, 4);
            var body = png.Slice(position + 8, length);
            if (type.SequenceEqual("IHDR"u8))
            {
                width = BinaryPrimitives.ReadInt32BigEndian(body);
                height = BinaryPrimitives.ReadInt32BigEndian(body[4..]);
                var bitDepth = body[8];
                channels = body[9] switch { 0 => 1, 2 => 3, 4 => 2, 6 => 4, _ => 0 };
                if (bitDepth != 8 || channels == 0 || body[12] != 0 || width <= 0 || height <= 0 || (long)width * height > 64_000_000)
                {
                    return false;
                }
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                data.Write(body);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                break;
            }

            position += 12 + length;
        }

        return channels > 0 && Pixels(data, width, height, channels) is { } pixels && AllSame(pixels, channels);
    }

    private static byte[]? Pixels(MemoryStream data, int width, int height, int channels)
    {
        var stride = width * channels;
        var raw = new byte[(stride + 1) * height];
        try
        {
            data.Position = 0;
            using var inflate = new ZLibStream(data, CompressionMode.Decompress, leaveOpen: true);
            inflate.ReadExactly(raw);
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
        {
            return null;
        }

        var pixels = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            var filter = raw[y * (stride + 1)];
            var source = raw.AsSpan(y * (stride + 1) + 1, stride);
            var row = pixels.AsSpan(y * stride, stride);
            var above = y > 0 ? pixels.AsSpan((y - 1) * stride, stride) : [];
            for (var x = 0; x < stride; x++)
            {
                int left = x >= channels ? row[x - channels] : 0;
                int up = y > 0 ? above[x] : 0;
                int corner = x >= channels && y > 0 ? above[x - channels] : 0;
                row[x] = filter switch
                {
                    0 => source[x],
                    1 => (byte)(source[x] + left),
                    2 => (byte)(source[x] + up),
                    3 => (byte)(source[x] + ((left + up) >> 1)),
                    4 => (byte)(source[x] + Paeth(left, up, corner)),
                    _ => source[x],
                };
            }

            if (filter > 4)
            {
                return null;
            }
        }

        return pixels;
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static bool AllSame(byte[] pixels, int channels)
    {
        var first = pixels.AsSpan(0, channels);
        for (var i = channels; i < pixels.Length; i += channels)
        {
            if (!pixels.AsSpan(i, channels).SequenceEqual(first))
            {
                return false;
            }
        }

        return true;
    }
}
