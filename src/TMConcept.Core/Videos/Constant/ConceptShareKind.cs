namespace TMConcept.Core.Videos;

/// <summary>Where a video's share of its concept's generation comes from (<see cref="VideoCostBreakdown.Share"/>).</summary>
public enum ConceptShareKind
{
    /// <summary>Stored with the video when it was made.</summary>
    Recorded = 0,

    /// <summary>Not stored (a free run stores none), worked out from the generation run that is still there.</summary>
    FromRun = 1,

    /// <summary>The concept was written by hand: no generation, nothing to share.</summary>
    Handwritten = 2,

    /// <summary>A later version: the share is counted with the first version of the video.</summary>
    FirstVersion = 3,

    /// <summary>Neither stored nor known any more (the concept or its run was deleted); never guessed.</summary>
    Unknown = 4,
}
