namespace TMConcept.Core.DiskSpace;

/// <summary>
/// Who needs a component: a profile and the step whose saved choice needs it (<see cref="ProfileName"/> and <see cref="Step"/> empty = the
/// app itself, for example ffmpeg for voice samples and the final video).
/// </summary>
public sealed record DiskUse(string ProfileName, string Step)
{
    /// <summary>A need of the app itself.</summary>
    public bool IsApp => ProfileName.Length == 0 && Step.Length == 0;
}
