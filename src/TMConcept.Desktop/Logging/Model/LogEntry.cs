using System.Globalization;

namespace TMConcept.Desktop.Logging;

/// <summary>
/// One entry of a log file as the log page shows it: when, its level (<c>Information</c>, <c>Warning</c>, <c>Error</c>, <c>Critical</c>), the
/// process that wrote it (<see cref="LogRules.AppKind"/> or <see cref="LogRules.SpeechWorkerKind"/>), its category and event id, the message,
/// and the lines that followed it (an exception with its stack).
/// </summary>
public sealed record LogEntry(DateTimeOffset At, string Level, string Source, string Category, int EventId, string Message, string Detail)
{
    /// <summary>The time as shown (<c>14:03:21.517</c>).</summary>
    public string Time => At.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

    /// <summary>The last part of the category (<c>VideoPipeline</c>).</summary>
    public string ShortCategory => Category.Length == 0 ? string.Empty : Category[(Category.LastIndexOf('.') + 1)..];

    /// <summary>Whether it is a warning.</summary>
    public bool IsWarning => Level == "Warning";

    /// <summary>Whether it is an error (or critical).</summary>
    public bool IsError => Level is "Error" or "Critical";

    /// <summary>Whether lines followed it.</summary>
    public bool HasDetail => Detail.Length > 0;

    /// <summary>The level's rank: 2 information (and the header lines), 3 warning, 4 error, 5 critical.</summary>
    public int Rank => Level switch
    {
        "Warning" => 3,
        "Error" => 4,
        "Critical" => 5,
        _ => 2,
    };

    /// <summary>The entry as one text block, as it stands in the file (for copying).</summary>
    public string Text => At.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture) + " [" + Level + "] " + Source + " " + Category
        + (EventId != 0 ? "[" + EventId.ToString(CultureInfo.InvariantCulture) + "]" : string.Empty) + ": " + Message
        + (Detail.Length > 0 ? Environment.NewLine + Detail : string.Empty);
}
