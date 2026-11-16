using Microsoft.Extensions.Logging;

namespace TMConcept.Core.Activity;

/// <summary>Event ids that change where a log line goes besides the log file.</summary>
public static class LogEvents
{
    /// <summary>
    /// The line's failure is also shown on screen in the user's language by the code that catches it (a failed video's red line, a failed
    /// backup): the log file keeps it, but it is not forwarded to the activity panel. Code logs first and adds the activity line afterwards,
    /// so without this one failure showed up as three or four lines there (the 0.1.16 review: engine, pipeline, assembler, the red line).
    /// </summary>
    public static readonly EventId OnScreen = new(9001, nameof(OnScreen));
}
