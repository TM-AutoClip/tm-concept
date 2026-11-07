using System.Buffers.Binary;

namespace TMConcept.Core.Speech;

/// <summary>
/// Reads the length of a PCM / float WAV file from its header (no decoding), writes mono 16-bit PCM files and joins WAV files of one format
/// with silences between them (a scene's spoken parts and its pauses).
/// </summary>
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

    /// <summary>
    /// Joins <paramref name="parts"/> into <paramref name="output"/>: each part is a file (null = none) and the silence after it. Every file
    /// must have the same format (one voice writes one format); the silence is zero samples of that format. Without any file a silent
    /// 16-bit 24 kHz mono file is written. Returns the length in milliseconds.
    /// </summary>
    public static long Join(IReadOnlyList<(string? File, int SilenceAfterMs)> parts, string output)
    {
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentException.ThrowIfNullOrEmpty(output);
        byte[]? format = null;
        using var data = new MemoryStream();
        var pendingSilence = 0;
        foreach (var (file, silence) in parts)
        {
            if (file is not null)
            {
                var (fmt, samples) = Read(file);
                if (format is null)
                {
                    format = fmt;
                }
                else if (!format.AsSpan().SequenceEqual(fmt))
                {
                    throw new InvalidDataException("The parts of a narration have different WAV formats.");
                }

                Silence(data, format, pendingSilence);
                pendingSilence = 0;
                data.Write(samples);
            }

            pendingSilence += Math.Max(0, silence);
        }

        // A silence at the end stays (a pause after the last sentence of a scene); a scene made only of pauses is silence.
        format ??= Pcm16Format(DefaultRate);
        Silence(data, format, pendingSilence);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        using (var stream = File.Create(output))
        {
            Span<byte> number = stackalloc byte[4];
            stream.Write("RIFF"u8);
            BinaryPrimitives.WriteInt32LittleEndian(number, (int)(4 + 8 + format.Length + 8 + data.Length));
            stream.Write(number);
            stream.Write("WAVEfmt "u8);
            BinaryPrimitives.WriteInt32LittleEndian(number, format.Length);
            stream.Write(number);
            stream.Write(format);
            stream.Write("data"u8);
            BinaryPrimitives.WriteInt32LittleEndian(number, (int)data.Length);
            stream.Write(number);
            data.Position = 0;
            data.CopyTo(stream);
        }

        return DurationMs(output);
    }

    /// <summary>Sample rate of a silent file made without any part.</summary>
    private const int DefaultRate = 24_000;

    private static byte[] Pcm16Format(int sampleRate)
    {
        var format = new byte[16];
        BinaryPrimitives.WriteInt16LittleEndian(format.AsSpan(0), 1);
        BinaryPrimitives.WriteInt16LittleEndian(format.AsSpan(2), 1);
        BinaryPrimitives.WriteInt32LittleEndian(format.AsSpan(4), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(format.AsSpan(8), sampleRate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(format.AsSpan(12), 2);
        BinaryPrimitives.WriteInt16LittleEndian(format.AsSpan(14), 16);
        return format;
    }

    private static void Silence(Stream data, byte[] format, int milliseconds)
    {
        if (milliseconds <= 0)
        {
            return;
        }

        var byteRate = BinaryPrimitives.ReadInt32LittleEndian(format.AsSpan(8, 4));
        var block = Math.Max(1, (int)BinaryPrimitives.ReadInt16LittleEndian(format.AsSpan(12, 2)));
        var bytes = (long)Math.Round(byteRate * milliseconds / 1000.0) / block * block;
        data.Write(new byte[bytes]);
    }

    /// <summary>The fmt chunk and the sample bytes of a WAV file.</summary>
    private static (byte[] Format, byte[] Samples) Read(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[12];
        stream.ReadExactly(header);
        if (!header[..4].SequenceEqual("RIFF"u8) || !header[8..12].SequenceEqual("WAVE"u8))
        {
            throw new InvalidDataException("Not a WAV file.");
        }

        byte[]? format = null;
        Span<byte> chunk = stackalloc byte[8];
        while (stream.Read(chunk) == 8)
        {
            var size = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
            if (chunk[..4].SequenceEqual("fmt "u8))
            {
                format = new byte[size];
                stream.ReadExactly(format);
                if (size % 2 == 1)
                {
                    stream.Seek(1, SeekOrigin.Current);
                }
            }
            else if (chunk[..4].SequenceEqual("data"u8))
            {
                var length = size == uint.MaxValue || size == 0 ? stream.Length - stream.Position : size;
                var samples = new byte[length];
                stream.ReadExactly(samples);
                return (format ?? throw new InvalidDataException("The WAV file has no format."), samples);
            }
            else
            {
                stream.Seek(size + (size % 2), SeekOrigin.Current);
            }
        }

        throw new InvalidDataException("The WAV file has no data.");
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
