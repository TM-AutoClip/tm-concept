using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace TMConcept.Desktop.Logging;

/// <summary>
/// The log file of one process kind: <c>&lt;kind&gt;-yyyy-MM-dd.log</c> by the local date, a new file at midnight while the program runs. Each
/// start and each new file begins with <see cref="AppIdentity.Header"/>. The file is opened for appending and shared (another process of the
/// same kind, or a problem report reading it, can use it at the same time); every entry is handed to the system at once and written
/// through to the disk (<c>Flush(true)</c>) right after a warning or worse, otherwise at most <see cref="LogRules.DiskFlushEvery"/> later
/// (so Explorer shows the real size of the open file and the lines survive a crash). Processes of the same kind take a named lock around
/// "go to the end and write", so a second start or a second worker never overwrites lines of the other. After the daily size limit one
/// warning line is written and nothing more until the next day. Old files are swept (<see cref="LogRetention"/>) at start and
/// at every day change; a file that cannot be deleted is written as a warning line. When a day's file cannot be opened at first, the
/// header and the sweep come with the first entry that opens it.
/// </summary>
public sealed class DailyLogFile : IDisposable
{
    private const string Category = "TMConcept.Logging";
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private readonly string _kind;
    private readonly TimeProvider _clock;
    private readonly long _maxBytes;
    private readonly LogScrubber _scrubber;
    private readonly Mutex? _shared;
    private readonly Lock _gate = new();
    private FileStream? _stream;
    private DateOnly _day = DateOnly.MinValue;
    private DateOnly _headerDay = DateOnly.MinValue;
    private readonly ITimer _flusher;
    private bool _full;
    private bool _disposed;
    private bool _dirty;
    private long _flushedAt;

    /// <summary>Opens today's file of <paramref name="kind"/> in <paramref name="directory"/> (created if missing) and writes the header.</summary>
    public DailyLogFile(string directory, string kind, TimeProvider clock, long maxBytes = LogRules.MaxFileBytes, LogScrubber? scrubber = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentException.ThrowIfNullOrEmpty(kind);
        ArgumentNullException.ThrowIfNull(clock);
        Folder = directory;
        _kind = kind;
        _clock = clock;
        _maxBytes = maxBytes;
        _scrubber = scrubber ?? LogScrubber.ForCurrentUser;
        Directory.CreateDirectory(directory);
        _shared = SharedLock(directory, kind);
        lock (_gate)
        {
            Open(Today());
            ToDisk();
        }

        _flusher = clock.CreateTimer(_ => FlushPending(), null, LogRules.DiskFlushEvery, LogRules.DiskFlushEvery);
    }

    /// <summary>How many times the file was written through to the disk (tests read it).</summary>
    internal int DiskFlushes { get; private set; }

    /// <summary>The log folder.</summary>
    public string Folder { get; }

    /// <summary>The file written now.</summary>
    public string CurrentFile => Path.Combine(Folder, LogFileName.For(_kind, _day));

    /// <summary>Appends one Information entry (thread-safe; a new day opens a new file first).</summary>
    public void Write(string entry) => Write(entry, LogLevel.Information);

    /// <summary>
    /// Appends one entry of <paramref name="level"/> (thread-safe; a new day opens a new file first). A warning or worse is written through to
    /// the disk at once, any other entry within <see cref="LogRules.DiskFlushEvery"/>.
    /// </summary>
    public void Write(string entry, LogLevel level)
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
            _dirty = true;
            if (level >= LogLevel.Warning || _clock.GetElapsedTime(_flushedAt) >= LogRules.DiskFlushEvery)
            {
                ToDisk();
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _flusher.Dispose();
        lock (_gate)
        {
            if (!_disposed)
            {
                ToDisk();
            }

            _disposed = true;
            _stream?.Dispose();
            _stream = null;
            _shared?.Dispose();
        }
    }

    /// <summary>The name of the lock the processes writing this folder's files of <paramref name="kind"/> share (no path in it).</summary>
    internal static string LockName(string directory, string kind)
    {
        var full = Path.GetFullPath(directory).TrimEnd('\\', '/');
        var key = OperatingSystem.IsWindows() ? full.ToUpperInvariant() : full;
        return "TMConcept-log-" + kind + "-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];
    }

    /// <summary>The shared lock; <c>null</c> when the system refuses it (then each process still writes at the end of the file).</summary>
    private static Mutex? SharedLock(string directory, string kind)
    {
        try
        {
            return new Mutex(false, LockName(directory, kind));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or WaitHandleCannotBeOpenedException or PlatformNotSupportedException)
        {
            System.Diagnostics.Debug.WriteLine("The shared log lock could not be created: " + ex.Message);
            return null;
        }
    }

    private DateOnly Today() => DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);

    /// <summary>The timer: writes through what is still waiting.</summary>
    private void FlushPending()
    {
        lock (_gate)
        {
            if (_dirty && !_disposed)
            {
                ToDisk();
            }
        }
    }

    /// <summary>Writes the system's buffer of the file through to the disk (inside the gate); a failure only loses this write-through.</summary>
    private void ToDisk()
    {
        _dirty = false;
        _flushedAt = _clock.GetTimestamp();
        if (_stream is null)
        {
            return;
        }

        try
        {
            _stream.Flush(flushToDisk: true);
            DiskFlushes++;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine("Log file flush failed: " + ex.Message);
        }
    }

    private void Open(DateOnly today)
    {
        _stream?.Dispose();
        _stream = null;
        if (today != _day)
        {
            _full = false;
        }

        _day = today;
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

        if (_headerDay == today)
        {
            // Reopened after a write failure on the same day: no second header, no second sweep.
            return;
        }

        _headerDay = today;
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

    private string Entry(LogLevel level, string message) => _scrubber.Scrub(LogLine.Format(_clock.GetLocalNow(), level, Category, default, message, null));

    private void Append(string entry, bool warnWhenFull)
    {
        if (_stream is null || _full)
        {
            return;
        }

        var locked = TakeShared();
        try
        {
            // Another process of the same kind may have written meanwhile: always at the end, under the shared lock.
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
        finally
        {
            if (locked)
            {
                _shared!.ReleaseMutex();
            }
        }
    }

    /// <summary>Takes the shared lock; a process that died holding it leaves it to the next one. Waits at most 2 s, then writes anyway.</summary>
    private bool TakeShared()
    {
        try
        {
            return _shared?.WaitOne(TimeSpan.FromSeconds(2)) ?? false;
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
    }
}
