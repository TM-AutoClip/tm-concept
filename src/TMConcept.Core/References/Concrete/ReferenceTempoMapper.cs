using System.Globalization;
using System.Text;
using TMConcept.Core.Scenes;
using TMConcept.Core.Speech;
using TMConcept.Core.Styles;

namespace TMConcept.Core.References;

/// <summary>
/// What a measured reference (<see cref="ReferenceTempoProfile"/>) means for a template (S1, 0.1.52). The narration tempo is the nearest of
/// the three by changes every 10 seconds (on a log scale: <see cref="NarrationTempoTargets"/> 4 / 3 / 1.5); the plan's targets are the
/// reference's own numbers (cuts every 10 s between 1 and 6, camera share between 0.1 and 0.9) with the ease of its camera moves in the
/// tempo's family first (out → expo / power2 / sine out, in → … in, in-out → … inOut) and always checked by the plan rule
/// (<see cref="Plans.TempoTargetRule"/>), both too few and too many. The transition energy is the tempo's. The palette suggestion is
/// <see cref="PaletteDesigner.FromBase"/> of its most colourful main colour, light when the reference is bright; it is taken only when the
/// user agrees. Pure.
/// </summary>
public static class ReferenceTempoMapper
{
    /// <summary>Fewest changes every 10 s a reference target asks for.</summary>
    public const double MinChanges = 1;

    /// <summary>Most changes every 10 s a reference target asks for (a plan cannot change more often than its sentences anyway).</summary>
    public const double MaxChanges = 6;

    /// <summary>Lowest camera share a reference target asks for.</summary>
    public const double MinCameraShare = 0.1;

    /// <summary>Highest camera share a reference target asks for.</summary>
    public const double MaxCameraShare = 0.9;

    private const double Colourful = 10;

    /// <summary>The template's tempo, the plan's targets, the transition energy and a palette suggestion of <paramref name="profile"/>.</summary>
    public static ReferenceSuggestion Suggest(ReferenceTempoProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var tempo = NearestTempo(profile.CutsPer10Seconds);
        return new ReferenceSuggestion(tempo, Target(profile, tempo), TransitionCatalog.EnergyOf(tempo), Palette(profile));
    }

    /// <summary>The narration tempo whose changes every 10 seconds are nearest to <paramref name="cutsPer10Seconds"/> (log scale).</summary>
    public static NarrationTempo NearestTempo(double cutsPer10Seconds)
    {
        var value = Math.Max(0.1, cutsPer10Seconds);
        return new[] { NarrationTempo.Tight, NarrationTempo.Natural, NarrationTempo.Relaxed }
            .OrderBy(t => Math.Abs(Math.Log(value / NarrationTempos.Targets(t).BeatsPer10Seconds))).First();
    }

    /// <summary>The plan's targets of <paramref name="profile"/> for a video at <paramref name="tempo"/>.</summary>
    public static TempoTarget Target(ReferenceTempoProfile profile, NarrationTempo tempo)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var changes = Math.Round(Math.Clamp(profile.CutsPer10Seconds, MinChanges, MaxChanges), 1);
        var camera = Math.Round(Math.Clamp(profile.CameraShare, MinCameraShare, MaxCameraShare), 2);
        var eases = new List<string> { Ease(profile.Ease, tempo) };
        eases.AddRange(NarrationTempos.Targets(tempo).Eases.Where(e => !eases.Contains(e, StringComparer.Ordinal)));
        return new TempoTarget(changes, camera, eases, Checked: true);
    }

    /// <summary>
    /// The plan's targets of a template's reference (null without one): the reference's numbers at the template's own tempo (the user may
    /// have changed the tempo after the reference was applied; the eases follow the tempo, the numbers follow the reference).
    /// </summary>
    public static TempoTarget? Target(VideoStyle? style) =>
        style?.Reference is { } reference ? Target(reference, style.Tempo) : null;

    /// <summary>The GSAP ease of a camera move of <paramref name="shape"/> at <paramref name="tempo"/> (one of <see cref="Plans.PlanEases.All"/>).</summary>
    public static string Ease(EaseShape shape, NarrationTempo tempo)
    {
        var family = tempo switch { NarrationTempo.Tight => "expo", NarrationTempo.Relaxed => "sine", _ => "power2" };
        return shape switch
        {
            EaseShape.Out => family + ".out",
            EaseShape.In => (tempo == NarrationTempo.Tight ? "power3" : family) + ".in",
            _ => (tempo == NarrationTempo.Tight ? "power3" : family) + ".inOut",
        };
    }

    /// <summary>A palette from the reference's most colourful main colour (a grey film: its first colour); null without colours.</summary>
    public static VideoPalette? Palette(ReferenceTempoProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var colours = profile.Palette.Where(c => PaletteColors.TryParse(c, out _)).ToList();
        if (colours.Count == 0)
        {
            return null;
        }

        var chosen = colours.Select(c => (Hex: c, Chroma: Chroma(c))).OrderByDescending(c => c.Chroma).First();
        var basis = chosen.Chroma >= Colourful ? chosen.Hex : colours[0];
        return PaletteDesigner.FromBase(basis, light: profile.LabL >= 60);
    }

    /// <summary>
    /// The emphasis moments of the plan request (S1): each peak of the reference as the scene that plays at the same share of this video, once
    /// per scene. <paramref name="sceneSeconds"/> are the scenes' lengths in order; empty without peaks.
    /// </summary>
    public static IReadOnlyList<(int Scene, double Share)> Moments(ReferenceTempoProfile? profile, IReadOnlyList<double> sceneSeconds)
    {
        ArgumentNullException.ThrowIfNull(sceneSeconds);
        if (profile is null || profile.Peaks.Count == 0 || sceneSeconds.Count == 0)
        {
            return [];
        }

        var total = sceneSeconds.Sum();
        if (total <= 0)
        {
            return [];
        }

        var moments = new List<(int, double)>();
        foreach (var share in profile.Peaks.Where(p => p is >= 0 and <= 1).Order())
        {
            var at = share * total;
            var elapsed = 0.0;
            var scene = sceneSeconds.Count;
            for (var i = 0; i < sceneSeconds.Count; i++)
            {
                elapsed += sceneSeconds[i];
                if (at < elapsed)
                {
                    scene = i + 1;
                    break;
                }
            }

            if (moments.All(m => m.Item1 != scene))
            {
                moments.Add((scene, share));
            }
        }

        return moments;
    }

    /// <summary>The one line of the plan request about the reference (empty without one): its targets and its emphasis moments.</summary>
    public static string PlanText(VideoStyle? style, IReadOnlyList<double> sceneSeconds)
    {
        ArgumentNullException.ThrowIfNull(sceneSeconds);
        if (style?.Reference is not { } reference)
        {
            return string.Empty;
        }

        var c = CultureInfo.InvariantCulture;
        var text = new StringBuilder("Reference video: the user wants the tempo of a reference video");
        if (reference.CameraDirection != CameraDirection.None)
        {
            text.Append(c, $"; its camera mostly moves {reference.CameraDirection.ToString().ToLowerInvariant()}");
        }

        text.Append('.');
        if (Moments(reference, sceneSeconds) is { Count: > 0 } moments)
        {
            text.Append(" Emphasis moments (give these scenes the strongest beat: a camera push, a counter, a highlight): ");
            text.Append(string.Join(", ", moments.Select(m => string.Create(c, $"scene {m.Scene} (at {m.Share * 100:0}% of the video)"))));
            text.Append('.');
        }

        return text.ToString();
    }

    private static double Chroma(string hex)
    {
        if (!PaletteColors.TryParse(hex, out var colour))
        {
            return 0;
        }

        var (_, a, b) = LabColor.FromRgb(colour.R, colour.G, colour.B);
        return Math.Sqrt((a * a) + (b * b));
    }
}
