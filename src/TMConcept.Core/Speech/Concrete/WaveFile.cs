using System.Buffers.Binary;

namespace TMConcept.Core.Speech;

/// <summary>Reads the length of a PCM / float WAV file from its header (no decoding) and writes mono 16-bit PCM files.</summary>
public static class WaveFile
{
    /// <summary>Writes mono samples in [-1, 1] as a 16-bit PCM WAV file (the folder is created; values outside the range are clipped).</summary>
    public static void WritePcm16(string path, ReadOnlySpan<float> samples, int sampleRate)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var data = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            var value = (short)Math.Round(Math.Clamp(samples[i], -1f, 1f) * short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(i * 2, 2), value);
        }

        Span<byte> header = stackalloc byte[44];
        "RIFF"u8.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], 36 + data.Length);
        "WAVEfmt "u8.CopyTo(header[8..]);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(header[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(header[22..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(header[24..], sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(header[28..], sampleRate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(header[32..], 2);
        BinaryPrimitives.WriteInt16LittleEndian(header[34..], 16);
        "data"u8.CopyTo(header[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(header[40..], data.Length);
        using var stream = File.Create(path);
        stream.Write(header);
        stream.Write(data);
    }

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
