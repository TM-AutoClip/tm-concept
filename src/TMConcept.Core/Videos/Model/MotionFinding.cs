using System.Globalization;

namespace TMConcept.Core.Videos;

/// <summary>
/// One finding of the motion check (<see cref="MotionCheck"/>): its warning code (<see cref="VideoWarnings.Motion"/>) and where it is in the
/// video (seconds; a moment has the same start and end).
/// </summary>
public sealed record MotionFinding(string Code, double From, double To)
{
    /// <summary>For the log: the code and the time.</summary>
    public override string ToString() => From.Equals(To)
        ? string.Create(CultureInfo.InvariantCulture, $"{Code} at {From:0.00} s")
        : string.Create(CultureInfo.InvariantCulture, $"{Code} {From:0.00}–{To:0.00} s");

    /// <summary>The moment a frame strip shows (the middle of a freeze, the start of anything else).</summary>
    public double StripAt => Code == VideoWarnings.MotionFreeze ? (From + To) / 2 : From;

    /// <summary>
    /// How long its strip covers: a freeze, a fade or the end over <see cref="MotionCheckLimits.WideStripSeconds"/>; a cut, a black frame or a
    /// flash frame by frame (0: a short flash could fall between spaced frames).
    /// </summary>
    public double StripSeconds => Code is VideoWarnings.MotionFreeze or VideoWarnings.MotionFade or VideoWarnings.MotionEndBlack
        ? MotionCheckLimits.WideStripSeconds
        : 0;

    /// <summary>The file name of its frame strip.</summary>
    public string StripName => string.Create(CultureInfo.InvariantCulture, $"{Code.Replace("motion_", string.Empty, StringComparison.Ordinal)}-{StripAt:0.00}s.png");
}
