namespace TMConcept.Core.Profiles;

/// <summary>Limits of a profile (channel).</summary>
public static class ProfileLimits
{
    /// <summary>Longest channel name.</summary>
    public const int NameMax = 80;

    /// <summary>Longest channel concept text.</summary>
    public const int ConceptTextMax = 6000;

    /// <summary>Shortest target video length (seconds).</summary>
    public const int SecondsMin = 15;

    /// <summary>Longest target video length (seconds; long videos come in a later version).</summary>
    public const int SecondsMax = 180;

    /// <summary>Slowest and fastest speech speed.</summary>
    public const double SpeedMin = 0.5;

    /// <summary>Fastest speech speed.</summary>
    public const double SpeedMax = 2.0;

    /// <summary>Longest sample image caption.</summary>
    public const int CaptionMax = 200;

    /// <summary>Most sample images in a profile.</summary>
    public const int ImagesMax = 24;
}
