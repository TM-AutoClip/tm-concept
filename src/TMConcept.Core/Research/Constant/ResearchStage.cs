namespace TMConcept.Core.Research;

/// <summary>Where a research run is (shown in the section and in the activity panel).</summary>
public enum ResearchStage
{
    /// <summary>Asking the backends.</summary>
    Searching,

    /// <summary>Fetching a picked page.</summary>
    Fetching,

    /// <summary>Saving a fetched page into the profile's documents (text, passages, vectors).</summary>
    Saving,

    /// <summary>Done.</summary>
    Done,
}
