namespace TMConcept.Desktop.Startup;

/// <summary>What the start's offer of a newer version came to (<see cref="IStartUpdateOffer"/>).</summary>
public enum StartUpdateOutcome
{
    /// <summary>No newer version (or the look is turned off, or it failed): the start goes on.</summary>
    None,

    /// <summary>A newer version was offered and put off ("Later", or the window closed): the start goes on.</summary>
    Later,

    /// <summary>The newer version was installed: the app restarts as it, so this start asks nothing more.</summary>
    Installed,

    /// <summary>The look had not answered within the start's wait: it is offered when it answers, after the start's questions.</summary>
    Pending,
}
