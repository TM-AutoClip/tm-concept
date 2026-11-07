using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Security;

namespace TMConcept.Desktop.Logging;

/// <summary>
/// <see cref="IActivityLog"/>: every line goes to the log file, complete, for finding a problem later (a line that names a profile or a
/// title is written with placeholders instead of the names). Since 0.1.7 there is no activity panel in the window (the developer's
/// choice): what the user must see — money spent, a failure — is shown where it belongs (the page's own line, a queue row, the video's
/// details) or as a corner notification by the caller.
/// </summary>
public sealed class ActivityLog(ILogger<ActivityLog> logger) : IActivityLog
{
    /// <inheritdoc />
    public void Add(ActivityLevel level, string text) => Add(level, text, text);

    /// <inheritdoc />
    public void Add(ActivityLevel level, string text, string fileText)
    {
        var line = Redactor.Redact(fileText);
        switch (level)
        {
            case ActivityLevel.Error:
                logger.LogError("Activity: {Text}", line);
                break;
            case ActivityLevel.Warning:
                logger.LogWarning("Activity: {Text}", line);
                break;
            default:
                logger.LogInformation("Activity: {Text}", line);
                break;
        }
    }
}
