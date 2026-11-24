namespace TMConcept.Core.Speech;

/// <summary>
/// What a narration tempo asks of the drawer's plan (F9, 0.1.42; <see cref="NarrationTempos.Targets"/>): about how many beats with something
/// new every 10 seconds (<paramref name="BeatsPer10Seconds"/>), the share of beats with a camera move (<paramref name="CameraShare"/>), the
/// ease family of the camera (<paramref name="Eases"/>, the first is the default) and whether the plan rules check them
/// (<paramref name="Checked"/>: natural is the tempo every plan before 0.1.42 was made for, so it is only asked for, never checked).
/// </summary>
public sealed record TempoTarget(double BeatsPer10Seconds, double CameraShare, IReadOnlyList<string> Eases, bool Checked)
{
    /// <summary>The ease of a camera move whose plan names none or an unknown one.</summary>
    public string DefaultEase => Eases[0];
}
