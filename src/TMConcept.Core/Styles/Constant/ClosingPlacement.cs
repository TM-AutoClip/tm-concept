namespace TMConcept.Core.Styles;

/// <summary>Where the closing call to action goes (new values are added at the end; stored as numbers).</summary>
public enum ClosingPlacement
{
    /// <summary>No closing.</summary>
    Off = 0,

    /// <summary>Spoken: the sentence is added to the narration of the last scene before speech.</summary>
    Voice = 1,

    /// <summary>On screen: an end card drawn by the app over the last seconds.</summary>
    Screen = 2,

    /// <summary>Spoken and on screen.</summary>
    Both = 3,
}
