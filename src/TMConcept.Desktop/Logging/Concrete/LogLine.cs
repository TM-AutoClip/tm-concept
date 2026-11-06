using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace TMConcept.Desktop.Logging;

/// <summary>One entry of a log file: <c>2026-11-06 10:15:02.123 +03:00 [Warning] Category[12]: message</c> (an exception follows on the next lines).</summary>
public static class LogLine
{
    /// <summary>Formats an entry.</summary>
    public static string Format(DateTimeOffset at, LogLevel level, string category, EventId eventId, string message, Exception? exception)
    {
        ArgumentNullException.ThrowIfNull(message);
        var text = new StringBuilder(64 + message.Length);
        text.Append(at.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture)).Append(" [").Append(level).Append("] ").Append(category);
        if (eventId.Id != 0)
        {
            text.Append('[').Append(eventId.Id.ToString(CultureInfo.InvariantCulture)).Append(']');
        }

        text.Append(": ").Append(message);
        if (exception is not null)
        {
            text.Append(Environment.NewLine).Append(exception);
        }

        return text.ToString();
    }
}
