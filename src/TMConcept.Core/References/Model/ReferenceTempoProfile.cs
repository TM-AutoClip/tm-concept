namespace TMConcept.Core.References;

/// <summary>
/// What a reference video measured ("bu videonun temposunda yap", S1, 0.1.52): only abstract numbers. No picture, frame or sound of the
/// reference is kept; besides the numbers only its name (a file's name, never its folder) or its web address. Part of a template
/// (<see cref="Styles.VideoStyle.Reference"/>, stored in its JSON; a template saved before has none). <see cref="ReferenceTempoMapper"/> turns
/// it into the template's tempo and the plan's targets.
/// </summary>
public sealed class ReferenceTempoProfile
{
    /// <summary>The reference: a file's name or the web address it was downloaded from.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>It was downloaded from a web address (yt-dlp).</summary>
    public bool FromUrl { get; set; }

    /// <summary>When it was measured (UTC).</summary>
    public DateTimeOffset MeasuredAtUtc { get; set; }

    /// <summary>How much of it was measured (seconds; at most <see cref="ReferenceLimits.MaxSeconds"/>).</summary>
    public double Seconds { get; set; }

    /// <summary>Cuts found.</summary>
    public int Cuts { get; set; }

    /// <summary>Cuts every 10 seconds.</summary>
    public double CutsPer10Seconds { get; set; }

    /// <summary>The mean length of a shot (seconds).</summary>
    public double AverageShotSeconds { get; set; }

    /// <summary>The share of shots with a camera move (0–1).</summary>
    public double CameraShare { get; set; }

    /// <summary>Which way the camera moves most.</summary>
    public CameraDirection CameraDirection { get; set; }

    /// <summary>How the camera moves start and stop.</summary>
    public EaseShape Ease { get; set; }

    /// <summary>The mean lightness (CIELAB L, 0–100).</summary>
    public double LabL { get; set; }

    /// <summary>The mean CIELAB a (green − / red +).</summary>
    public double LabA { get; set; }

    /// <summary>The mean CIELAB b (blue − / yellow +).</summary>
    public double LabB { get; set; }

    /// <summary>The mean brightness (0–1).</summary>
    public double Brightness { get; set; }

    /// <summary>The mean saturation (0–1).</summary>
    public double Saturation { get; set; }

    /// <summary>The main colours (#RRGGBB, most of the picture first; at most <see cref="ReferenceLimits.PaletteSize"/>).</summary>
    public List<string> Palette { get; set; } = [];

    /// <summary>It has sound louder than silence.</summary>
    public bool HasSound { get; set; }

    /// <summary>The tempo of its beat grid (beats a minute; 0 = none found).</summary>
    public double Bpm { get; set; }

    /// <summary>Its strongest moments as a share of its length (0–1, in order; at most <see cref="ReferenceLimits.Peaks"/>).</summary>
    public List<double> Peaks { get; set; } = [];
}
