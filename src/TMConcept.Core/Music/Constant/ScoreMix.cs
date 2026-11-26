namespace TMConcept.Core.Music;

/// <summary>
/// The mix of composed music (0.1.55, "Program composes"): each part's MIDI channel, channel volume, place between the speakers and how much
/// softer than the mood's velocity it plays. The tune stays soft, the music lies under a narration.
/// </summary>
public static class ScoreMix
{
    /// <summary>The held chords' channel.</summary>
    public const int PadChannel = 0;

    /// <summary>The bass's channel.</summary>
    public const int BassChannel = 1;

    /// <summary>The broken chords' channel.</summary>
    public const int ArpeggioChannel = 2;

    /// <summary>The tune's channel.</summary>
    public const int LeadChannel = 3;

    /// <summary>The held chords' volume.</summary>
    public const int PadVolume = 80;

    /// <summary>The bass's volume.</summary>
    public const int BassVolume = 96;

    /// <summary>The broken chords' volume.</summary>
    public const int ArpeggioVolume = 70;

    /// <summary>The tune's volume.</summary>
    public const int LeadVolume = 84;

    /// <summary>The drums' volume.</summary>
    public const int DrumVolume = 90;

    /// <summary>The broken chords a little to the left.</summary>
    public const int ArpeggioPan = 44;

    /// <summary>The tune a little to the right.</summary>
    public const int LeadPan = 84;

    /// <summary>How much softer the held chords play.</summary>
    public const int PadSofter = 10;

    /// <summary>How much softer the broken chords play.</summary>
    public const int ArpeggioSofter = 15;

    /// <summary>How much softer the tune plays.</summary>
    public const int LeadSofter = 8;

    /// <summary>How much louder the first beat of a bar is.</summary>
    public const int Accent = 8;
}
