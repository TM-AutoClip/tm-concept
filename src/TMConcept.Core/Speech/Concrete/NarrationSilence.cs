using System.Buffers.Binary;

namespace TMConcept.Core.Speech;

/// <summary>
/// Removes dead air from a spoken clip (<see cref="NarrationGaps"/>): the silence before the first word and after the last one is cut to a
/// short margin, and a pause inside the clip longer than <see cref="NarrationGaps.MaxPauseMs"/> is shortened to it (the middle of the pause
/// goes, its edges stay, so no word is touched). Every cut fades out and in over <see cref="NarrationGaps.FadeMs"/> (0.1.62). Loudness is the RMS of <see cref="NarrationGaps.WindowMs"/> windows over every channel.
/// Reads 16-bit PCM and 32-bit float WAV files (what the voices write); another format is left as it is (<see cref="Trim"/> says so).
/// </summary>
public static class NarrationSilence
{
    /// <summary>
    /// Trims <paramref name="path"/> in place. Returns the new length in milliseconds, or <c>null</c> when the format is not one this reads (the
    /// file is unchanged then). A clip that is silence only keeps one <see cref="NarrationGaps.MaxPauseMs"/> of it. The file is always written
    /// again: its edges fade in and out even when nothing is cut.
    /// </summary>
    public static long? Trim(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var (format, data) = WaveFile.Read(path);
        if (Layout(format) is not { } layout)
        {
            return null;
        }

        var windows = Windows(data, layout);
        var keep = Keep(windows, layout.WindowBytes(), data.Length);
        var output = new byte[keep.Sum(k => k.Length)];
        var at = 0;
        foreach (var (start, length) in keep)
        {
            data.AsSpan(start, length).CopyTo(output.AsSpan(at));
            // Every kept range fades in and out (0.1.62): its edges are the cuts, the clip's start and its end, where it meets a pause or the
            // next clip. Even a clip with nothing to cut gets its edges faded, so no join of the narration clicks.
            Fade(output.AsSpan(at, length), layout);
            at += length;
        }

        WaveFile.Write(path, format, output);
        return WaveFile.DurationMs(path);
    }

    /// <summary>The longest silence anywhere in the clip, in milliseconds (0 for an unknown format): what a listener hears as a gap.</summary>
    public static long LongestGapMs(string path) => Gaps(path).DefaultIfEmpty(0).Max();

    /// <summary>
    /// The silences of a clip in milliseconds, in order: the one before the first word first, the one after the last word last (each 0 when
    /// there is none). Empty for an unknown format.
    /// </summary>
    public static IReadOnlyList<long> Gaps(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var (format, data) = WaveFile.Read(path);
        if (Layout(format) is not { } layout)
        {
            return [];
        }

        var windows = Windows(data, layout);
        var gaps = new List<long>();
        var run = 0;
        var spoken = false;
        foreach (var silent in windows)
        {
            if (silent)
            {
                run++;
                continue;
            }

            if (run > 0 || !spoken)
            {
                gaps.Add((long)run * NarrationGaps.WindowMs);
            }

            run = 0;
            spoken = true;
        }

        gaps.Add((long)run * NarrationGaps.WindowMs);
        return gaps;
    }

    /// <summary>
    /// The spoken parts of a clip in milliseconds (every run of windows louder than <see cref="NarrationGaps.SilenceLevel"/>), in order; the
    /// background music ducks under them (0.1.44, <see cref="Media.MusicEnvelope"/>). <c>null</c> for a format this does not read; empty for
    /// a clip that is silence only.
    /// </summary>
    public static IReadOnlyList<Media.NarrationSpan>? Spoken(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var (format, data) = WaveFile.Read(path);
        if (Layout(format) is not { } layout)
        {
            return null;
        }

        var windows = Windows(data, layout);
        var spans = new List<Media.NarrationSpan>();
        var start = -1;
        for (var w = 0; w <= windows.Length; w++)
        {
            var loud = w < windows.Length && !windows[w];
            if (loud && start < 0)
            {
                start = w;
            }
            else if (!loud && start >= 0)
            {
                spans.Add(new Media.NarrationSpan((long)start * NarrationGaps.WindowMs, (long)w * NarrationGaps.WindowMs));
                start = -1;
            }
        }

        return spans;
    }

    /// <summary>The byte ranges kept: every loud window, and of each silent run only its allowed margins.</summary>
    private static List<(int Start, int Length)> Keep(bool[] windows, int windowBytes, int total)
    {
        var lead = Count(NarrationGaps.LeadKeepMs);
        var tail = Count(NarrationGaps.TailKeepMs);
        var pause = Count(NarrationGaps.MaxPauseMs);
        var first = Array.IndexOf(windows, false);
        if (first < 0)
        {
            // Nothing is said: one short silence, so a scene that was only silence stays a (short) scene.
            return [(0, Math.Min(total, pause * windowBytes))];
        }

        var last = Array.LastIndexOf(windows, false);
        var ranges = new List<(int Start, int Length)>();
        var from = Math.Max(0, first - lead);
        var i = first;
        while (i <= last)
        {
            if (!windows[i])
            {
                i++;
                continue;
            }

            // A silent run inside the clip: keep its first and last halves of the allowed pause.
            var end = i;
            while (end <= last && windows[end])
            {
                end++;
            }

            if (end - i > pause)
            {
                var head = pause / 2;
                ranges.Add((from * windowBytes, (i + head - from) * windowBytes));
                from = end - (pause - head);
            }

            i = end;
        }

        var stop = Math.Min(windows.Length, last + 1 + tail);
        var startByte = from * windowBytes;
        var endByte = Math.Min(total, stop * windowBytes);
        ranges.Add((startByte, endByte - startByte));
        return ranges.Where(r => r.Length > 0).ToList();
    }

    private static int Count(int milliseconds) => milliseconds / NarrationGaps.WindowMs;

    /// <summary>
    /// A linear fade in over the first and out over the last <see cref="NarrationGaps.FadeMs"/> of <paramref name="range"/> (whole frames; a
    /// range shorter than two fades fades over its halves). Applying it again only lowers the already quiet edges a little more.
    /// </summary>
    private static void Fade(Span<byte> range, SampleLayout layout)
    {
        var frameBytes = layout.Channels * layout.SampleBytes;
        var frames = range.Length / frameBytes;
        var fade = Math.Min(Math.Max(1, layout.Rate * NarrationGaps.FadeMs / 1000), frames / 2);
        for (var f = 0; f < fade; f++)
        {
            var gain = (double)f / fade;
            for (var c = 0; c < layout.Channels; c++)
            {
                var offset = c * layout.SampleBytes;
                layout.Scale(range.Slice((f * frameBytes) + offset, layout.SampleBytes), gain);
                layout.Scale(range.Slice(((frames - 1 - f) * frameBytes) + offset, layout.SampleBytes), gain);
            }
        }
    }

    /// <summary>Whether each window is silent (the last, shorter window is measured too).</summary>
    private static bool[] Windows(byte[] data, SampleLayout layout)
    {
        var windowBytes = layout.WindowBytes();
        var count = (data.Length + windowBytes - 1) / windowBytes;
        var windows = new bool[count];
        for (var w = 0; w < count; w++)
        {
            var start = w * windowBytes;
            var length = Math.Min(windowBytes, data.Length - start) / layout.SampleBytes * layout.SampleBytes;
            double sum = 0;
            var samples = length / layout.SampleBytes;
            for (var s = 0; s < samples; s++)
            {
                var value = layout.Sample(data.AsSpan(start + (s * layout.SampleBytes), layout.SampleBytes));
                sum += value * value;
            }

            windows[w] = samples == 0 || Math.Sqrt(sum / samples) < NarrationGaps.SilenceLevel;
        }

        return windows;
    }

    /// <summary>The sample layout of an fmt chunk this reads; <c>null</c> for anything but 16-bit PCM and 32-bit float.</summary>
    private static SampleLayout? Layout(byte[] format)
    {
        if (format.Length < 16)
        {
            return null;
        }

        var tag = BinaryPrimitives.ReadUInt16LittleEndian(format.AsSpan(0, 2));
        var channels = BinaryPrimitives.ReadUInt16LittleEndian(format.AsSpan(2, 2));
        var rate = BinaryPrimitives.ReadInt32LittleEndian(format.AsSpan(4, 4));
        var bits = BinaryPrimitives.ReadUInt16LittleEndian(format.AsSpan(14, 2));
        if (tag == 0xFFFE && format.Length >= 26)
        {
            // WAVE_FORMAT_EXTENSIBLE: the real tag is the first two bytes of the sub format.
            tag = BinaryPrimitives.ReadUInt16LittleEndian(format.AsSpan(24, 2));
        }

        if (channels == 0 || rate <= 0)
        {
            return null;
        }

        return (tag, bits) switch
        {
            (1, 16) => new SampleLayout(rate, channels, 2, Float: false),
            (3, 32) => new SampleLayout(rate, channels, 4, Float: true),
            _ => null,
        };
    }
}
