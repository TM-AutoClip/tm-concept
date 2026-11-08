namespace TMConcept.Core.Chat;

/// <summary>Where a chat call is (<see cref="ChatProgress"/>; new values are added at the end).</summary>
public enum ChatPhase
{
    /// <summary>The request was sent; no answer yet.</summary>
    Sent = 0,

    /// <summary>The answer started but no visible text yet (the model thinks).</summary>
    Thinking = 1,

    /// <summary>Visible text is arriving.</summary>
    Writing = 2,

    /// <summary>The whole answer arrived (also a cut-off one; the caller checks it).</summary>
    Done = 3,
}
