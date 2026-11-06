namespace TMConcept.Desktop.Logging;

/// <summary>
/// Rules of the log files: one file per day and process kind (<c>logs/app-2026-11-06.log</c>, <c>logs/speech-worker-2026-11-06.log</c>),
/// kept for <see cref="KeepDays"/> days, at most <see cref="MaxFileBytes"/> per file and day.
/// </summary>
public static class LogRules
{
    /// <summary>Days a log file is kept (by the date in its name): a file from 7 days ago stays, one from 8 days ago is deleted.</summary>
    public const int KeepDays = 7;

    /// <summary>Largest size of one day's file; after it one warning line is written and nothing more until the next day.</summary>
    public const long MaxFileBytes = 20L * 1024 * 1024;

    /// <summary>The window process.</summary>
    public const string AppKind = "app";

    /// <summary>The speech worker process (<c>--speech-worker</c>).</summary>
    public const string SpeechWorkerKind = "speech-worker";

    /// <summary>File extension of a log file.</summary>
    public const string Extension = ".log";

    /// <summary>Name prefix of the 0.1.0–0.1.3 files (<c>tmconcept-yyyyMMdd.log</c>); they are swept by the same date rule.</summary>
    public const string LegacyPrefix = "tmconcept-";

    /// <summary>Every process kind that writes a log file.</summary>
    public static readonly IReadOnlyList<string> Kinds = [AppKind, SpeechWorkerKind];
}
