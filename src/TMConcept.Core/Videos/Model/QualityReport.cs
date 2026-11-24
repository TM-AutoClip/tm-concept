using System.Text.Json.Serialization;

namespace TMConcept.Core.Videos;

/// <summary>
/// What the quality mode found in one video (F11 + F14, 0.1.50): the review rounds of the rendered frames and, in the careful mode, of the
/// local pictures, why they ended, the recurring character and the final critic's scores. Kept in the version folder as
/// <see cref="VideoFolders.Quality"/>/<see cref="VideoFolders.QualityReportFile"/> (no schema change); the Produced page shows it.
/// </summary>
public sealed class QualityReport
{
    /// <summary>The template's quality mode.</summary>
    public Styles.QualityMode Mode { get; set; }

    /// <summary>The reviewer ("provider/model").</summary>
    public string Reviewer { get; set; } = string.Empty;

    /// <summary>The review rounds of the rendered frames.</summary>
    public List<QualityRound> Frames { get; set; } = [];

    /// <summary>Why the frame rounds ended (<see cref="QualityStops"/>; empty: they did not run).</summary>
    public string FrameStop { get; set; } = string.Empty;

    /// <summary>The review rounds of the scene pictures made on this computer (careful mode, a local reviewer; empty otherwise).</summary>
    public List<QualityRound> Pictures { get; set; } = [];

    /// <summary>Why the picture rounds ended (<see cref="QualityStops"/>; empty: they did not run).</summary>
    public string PictureStop { get; set; } = string.Empty;

    /// <summary>The careful mode found faults the model's free code cannot be repaired for (a warning only).</summary>
    public bool NotRepaired { get; set; }

    /// <summary>The recurring character's description the image prompt call wrote (F14; empty: none).</summary>
    public string Character { get; set; } = string.Empty;

    /// <summary>A character sheet was drawn (careful mode, F14).</summary>
    public bool CharacterSheet { get; set; }

    /// <summary>The scenes whose picture did not match the character sheet (careful mode, F14).</summary>
    public List<int> CharacterMismatch { get; set; } = [];

    /// <summary>The final critic's headings with their scores (empty: it did not run).</summary>
    public List<CriticScore> Critic { get; set; } = [];

    /// <summary>The files to keep with the version (the last review sheet, the character sheet); work paths, not written into the report.</summary>
    [JsonIgnore]
    public List<string> Files { get; set; } = [];
}
