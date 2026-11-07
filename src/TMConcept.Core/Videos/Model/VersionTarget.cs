namespace TMConcept.Core.Videos;

/// <summary>
/// The version folder a run writes into, filled when the folder is made: a failure after that removes what the run wrote (the whole folder
/// when the run created it).
/// </summary>
public sealed class VersionTarget
{
    /// <summary>The version folder (null until the run makes it).</summary>
    public string? Folder { get; set; }

    /// <summary>Whether the run created the folder.</summary>
    public bool Created { get; set; }
}
