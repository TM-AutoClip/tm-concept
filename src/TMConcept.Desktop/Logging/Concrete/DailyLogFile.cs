using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace TMConcept.Desktop.Logging;

/// <summary>
/// The log file of one process kind: <c>&lt;kind&gt;-yyyy-MM-dd.log</c> by the local date, a new file at midnight while the program runs. Each
/// start and each new file begins with <see cref="AppIdentity.Header"/>. The file is opened for appending and shared (another process of the
/// same kind, or a problem report reading it, can use it at the same time); every entry is flushed. After the daily size limit one warning
/// line is written and nothing more until the next day. Old files are swept (<see cref="LogRetention"/>) at start and at every day change; a
/// file that cannot be deleted is written as a warning line.
/// </summary>
public sealed class DailyLogFile : IDisposable
{
    private const string Category = "TMConcept.Logging";
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private readonly string _kind;
    private readonly TimeProvider _clock;
    private readonly long _maxBytes;
    private readonly Lock _gate = new();
    private FileStream? _stream;
    private DateOnly _day;
    private bool _opened;
    private bool _full;
    private bool _disposed;

    /// <summary>Opens today's file of <paramref name="kind"/> in <paramref name="directory"/> (created if missing) and writes the header.</summary>
    public DailyLogFile(string directory, string kind, TimeProvider clock, long maxBytes = LogRules.MaxFileBytes)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentException.ThrowIfNullOrEmpty(kind);
        ArgumentNullException.ThrowIfNull(clock);
        Folder = directory;
        _kind = kind;
        _clock = clock;
        _maxBytes = maxBytes;
        Directory.CreateDirectory(directory);
        lock (_gate)
        {
            Open(Today());
        }
    }

    /// <summary>The log folder.</summary>
    public string Folder { get; }

    /// <summary>The file written now.</summary>
    public string CurrentFile => Path.Combine(Folder, LogFileName.For(_kind, _day));

    /// <summary>Appends one entry (thread-safe; a new day opens a new file first).</summary>
    public void Write(string entry)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var today = Today();
            if (today != _day || _stream is null)
            {
                Open(today);
            }

            Append(entry, warnWhenFull: true);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _stream?.Dispose();
            _stream = null;
        }
    }

    private DateOnly Today() => DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);

    private void Open(DateOnly today)
    {
        var newDay = !_opened || today != _day;
        _stream?.Dispose();
        _stream = null;
        _day = today;
        if (newDay)
        {
            _full = false;
        }

        try
        {
            _stream = new FileStream(CurrentFile, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The log itself cannot be written (a read-only or full disk): the debugger output is the last place left; the next entry tries again.
            System.Diagnostics.Debug.WriteLine("Log file could not be opened: " + ex.Message);
            return;
        }

        if (!newDay)
        {
            // Reopened after a write failure on the same day: no second header, no second sweep.
            return;
        }

        _opened = true;
        Append(AppIdentity.Header(_kind), warnWhenFull: false);
        foreach (var failure in Sweep(today))
        {
            Append(failure, warnWhenFull: false);
        }
    }

    private List<string> Sweep(DateOnly today)
    {
        var failures = new List<string>();
        try
        {
            LogRetention.Sweep(Folder, today, (name, ex) => failures.Add(Entry(LogLevel.Warning,
                "An old log file could not be deleted (" + name + "): " + ex.GetType().Name + ": " + ex.Message)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failures.Add(Entry(LogLevel.Warning, "The log folder could not be read to delete old files: " + ex.GetType().Name + ": " + ex.Message));
        }

        return failures;
    }

    private string Entry(LogLevel level, string message) => LogScrubber.ForCurrentUser.Scrub(LogLine.Format(_clock.GetLocalNow(), level, Category, default, message, null));

    private void Append(string entry, bool warnWhenFull)
    {
        if (_stream is null || _full)
        {
            return;
        }

        try
        {
            // Another process of the same kind may have written meanwhile: always at the end.
            var length = _stream.Seek(0, SeekOrigin.End);
            var bytes = Utf8.GetBytes(entry + Environment.NewLine);
            if (length + bytes.Length > _maxBytes)
            {
                _full = true;
                if (warnWhenFull)
                {
                    var megabytes = (_maxBytes / (1024.0 * 1024)).ToString("0.#", CultureInfo.InvariantCulture);
                    _stream.Write(Utf8.GetBytes(Entry(LogLevel.Warning,
                        "This log file reached its daily size limit (" + megabytes + " MB); nothing more is written to it until tomorrow") + Environment.NewLine));
                    _stream.Flush();
                }

                return;
            }

            _stream.Write(bytes);
            _stream.Flush();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or UnauthorizedAccessException or NotSupportedException)
        {
            System.Diagnostics.Debug.WriteLine("Log file write failed: " + ex.Message);
            _stream?.Dispose();
            _stream = null;
        }
    }
}
