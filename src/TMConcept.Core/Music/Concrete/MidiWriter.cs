using System.Text;

namespace TMConcept.Core.Music;

/// <summary>
/// Writes a score as a Standard MIDI File (0.1.55, "Program composes"; pure; format 1 as the MIDI 1.0 specification describes it): a first
/// track with the tempo and the 4/4 time signature, then one track per instrument with its name, instrument (not on the drum channel), volume,
/// place, reverb and notes. Running status is not used; at one tick a note's end comes before the next note's start, so a repeated note is
/// heard twice.
/// </summary>
public static class MidiWriter
{
    private const int MicrosecondsPerMinute = 60_000_000;
    private const int HeaderLength = 6;
    private const int Format = 1;
    private const int SevenBits = 0x7F;
    private const int MoreBytes = 0x80;
    private const byte QuarterAsPowerOfTwo = 2;
    private const byte ClocksPerClick = 24;
    private const byte ThirtySecondsPerQuarter = 8;
    private const byte Meta = 0xFF;
    private const byte NoteOff = 0x80;
    private const byte NoteOn = 0x90;
    private const byte Controller = 0xB0;
    private const byte ProgramChange = 0xC0;
    private const byte VolumeController = 7;
    private const byte PanController = 10;
    private const byte ReverbController = 91;
    private const byte TrackNameMeta = 0x03;
    private const byte TempoMeta = 0x51;
    private const byte TimeSignatureMeta = 0x58;
    private const byte EndOfTrackMeta = 0x2F;

    /// <summary>The bytes of the MIDI file of <paramref name="score"/>.</summary>
    public static byte[] Write(MusicScore score)
    {
        ArgumentNullException.ThrowIfNull(score);
        using var file = new MemoryStream();
        file.Write("MThd"u8);
        BigEndian(file, HeaderLength, 4);
        BigEndian(file, Format, 2);
        BigEndian(file, score.Tracks.Count + 1, 2);
        BigEndian(file, score.TicksPerQuarter, 2);
        Chunk(file, Conductor(score));
        foreach (var track in score.Tracks)
        {
            Chunk(file, Track(track, score.EndTick));
        }

        return file.ToArray();
    }

    /// <summary>A number as a MIDI variable-length quantity (seven bits a byte, the high bit set on every byte but the last).</summary>
    public static byte[] VariableLength(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        var bytes = new Stack<byte>();
        bytes.Push((byte)(value & SevenBits));
        while ((value >>= 7) > 0)
        {
            bytes.Push((byte)((value & SevenBits) | MoreBytes));
        }

        return [.. bytes];
    }

    private static byte[] Conductor(MusicScore score)
    {
        using var track = new MemoryStream();
        var tempo = MicrosecondsPerMinute / score.Bpm;
        Event(track, 0, [Meta, TempoMeta, 3, (byte)(tempo >> 16), (byte)(tempo >> 8), (byte)tempo]);

        // Beats a bar over quarter notes, the metronome's click every quarter note.
        Event(track, 0, [Meta, TimeSignatureMeta, 4, (byte)score.BeatsPerBar, QuarterAsPowerOfTwo, ClocksPerClick, ThirtySecondsPerQuarter]);
        Event(track, score.EndTick, [Meta, EndOfTrackMeta, 0]);
        return track.ToArray();
    }

    private static byte[] Track(ScoreTrack track, int end)
    {
        using var stream = new MemoryStream();
        var channel = (byte)track.Channel;
        var name = Encoding.ASCII.GetBytes(track.Name);
        Event(stream, 0, [Meta, TrackNameMeta, .. VariableLength(name.Length), .. name]);
        if (track.Channel != ScoreRules.DrumChannel)
        {
            Event(stream, 0, [(byte)(ProgramChange | channel), (byte)track.Program]);
        }

        Event(stream, 0, [(byte)(Controller | channel), VolumeController, (byte)track.Volume]);
        Event(stream, 0, [(byte)(Controller | channel), PanController, (byte)track.Pan]);
        Event(stream, 0, [(byte)(Controller | channel), ReverbController, ScoreRules.Reverb]);

        // Ends sort before starts at the same tick (false < true), then by pitch, so the file is the same every time.
        var events = track.Notes
            .SelectMany(n => new[] { (Tick: n.Start, On: true, n.Pitch, n.Velocity), (Tick: n.Start + n.Length, On: false, n.Pitch, Velocity: 0) })
            .OrderBy(e => e.Tick).ThenBy(e => e.On).ThenBy(e => e.Pitch)
            .ToList();
        var last = 0;
        foreach (var e in events)
        {
            Event(stream, e.Tick - last, [(byte)((e.On ? NoteOn : NoteOff) | channel), (byte)e.Pitch, (byte)e.Velocity]);
            last = e.Tick;
        }

        Event(stream, Math.Max(0, end - last), [Meta, EndOfTrackMeta, 0]);
        return stream.ToArray();
    }

    private static void Event(Stream stream, int delta, ReadOnlySpan<byte> data)
    {
        stream.Write(VariableLength(delta));
        stream.Write(data);
    }

    private static void Chunk(Stream file, byte[] body)
    {
        file.Write("MTrk"u8);
        BigEndian(file, body.Length, 4);
        file.Write(body);
    }

    private static void BigEndian(Stream stream, int value, int bytes)
    {
        for (var i = bytes - 1; i >= 0; i--)
        {
            stream.WriteByte((byte)(value >> (8 * i)));
        }
    }
}
