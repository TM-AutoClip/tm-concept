using System.Buffers.Binary;

namespace TMConcept.Core.Speech;

/// <summary>Reads the length of a PCM / float WAV file from its header (no decoding).</summary>
public static class WaveFile
{
    /// <summary>Length in milliseconds.</summary>
    public static long DurationMs(string path)
    {
        using var stream = File.OpenRead(path);
        return DurationMs(stream);
    }

    /// <summary>Length in milliseconds of a WAV stream.</summary>
    public static long DurationMs(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Span<byte> header = stackalloc byte[12];
        stream.ReadExactly(header);
        if (!header[..4].SequenceEqual("RIFF"u8) || !header[8..12].SequenceEqual("WAVE"u8))
        {
            throw new InvalidDataException("Not a WAV file.");
        }

        var byteRate = 0;
        Span<byte> chunk = stackalloc byte[8];
        while (stream.Read(chunk) == 8)
        {
            var size = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
            if (chunk[..4].SequenceEqual("fmt "u8))
            {
                var format = new byte[size];
                stream.ReadExactly(format);
                byteRate = BinaryPrimitives.ReadInt32LittleEndian(format.AsSpan(8, 4));
            }
            else if (chunk[..4].SequenceEqual("data"u8))
            {
                var bytes = size == uint.MaxValue || size == 0 ? stream.Length - stream.Position : size;
                return byteRate > 0 ? (long)Math.Round(bytes * 1000.0 / byteRate) : 0;
            }
            else
            {
                stream.Seek(size + (size % 2), SeekOrigin.Current);
            }
        }

        throw new InvalidDataException("The WAV file has no data.");
    }
}
