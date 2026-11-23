using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TMConcept.Desktop.Instance;

/// <summary>
/// One running app per data folder. The first start holds <c>instance.lock</c> in the data folder open without sharing for its whole life
/// (Windows: no second open; Linux: .NET takes an exclusive flock). A file lock is used, not a named mutex: on Linux .NET scopes a named
/// mutex to the login session, so a start from a terminal and one from the menu both counted as first, and the second one's start-up
/// clean-up then ended the first one's render processes and emptied its work folder. A named pipe (a Unix socket on Linux) carries "show
/// your window" from a second start to the first. A closing instance stops listening first (<see cref="StopListening"/>); a second start
/// that finds the lock held but nobody listening (the copy that started it, through an update or Setup, is still closing) waits for the lock
/// instead of ending unseen (<see cref="WaitForClosing"/>).
/// </summary>
public sealed class SingleInstance : IDisposable
{
    /// <summary>The lock file's name in the data folder.</summary>
    public const string LockFileName = "instance.lock";

    /// <summary>
    /// How long a second start waits for a closing instance's lock (its exit frees the graphics card and stops the app's own Ollama, at
    /// most some 20 seconds).
    /// </summary>
    public static readonly TimeSpan ClosingWait = TimeSpan.FromSeconds(60);

    private readonly FileStream? _lock;
    private readonly string _name;
    private readonly CancellationTokenSource _stop = new();
    private int _disposed;

    private SingleInstance(FileStream? lockFile, string name)
    {
        _lock = lockFile;
        _name = name;
        if (IsFirst)
        {
            _ = ListenAsync();
        }
    }

    /// <summary>Raised (on a pool thread) when a second start asks for the window.</summary>
    public event EventHandler? ShowRequested;

    /// <summary>Where the pipe's problems are written (set once logging exists; the lock is taken before it).</summary>
    public ILogger Logger { get; set; } = NullLogger.Instance;

    /// <summary>Whether this is the first instance (it holds the lock file).</summary>
    public bool IsFirst => _lock is not null;

    /// <summary>Takes the instance lock of the data folder (the folder must exist).</summary>
    public static SingleInstance Acquire(string dataDirectory)
    {
        var full = Path.GetFullPath(dataDirectory);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full.ToUpperInvariant())))[..16];
        var name = "TMConcept-" + hash;
        FileStream? lockFile;
        try
        {
            lockFile = new FileStream(Path.Combine(full, LockFileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ex) when (ex is not DirectoryNotFoundException and not PathTooLongException and not FileNotFoundException)
        {
            // Held by the running instance (sharing violation on Windows, flock on Linux): this is a second start.
            Console.Error.WriteLine("TM Concept is already running for this data folder (" + ex.GetType().Name + ").");
            lockFile = null;
        }

        return new SingleInstance(lockFile, name);
    }

    /// <summary>
    /// Asks the first instance to show its window; false when nobody answered (it is closing and stopped listening, or not listening yet).
    /// </summary>
    public bool SignalFirst()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", _name, PipeDirection.Out);
            client.Connect(2000);
            client.Write("show\n"u8);
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException)
        {
            Console.Error.WriteLine("TM Concept is already running: " + ex.Message);
            Logger.LogWarning(ex, "The running app could not be asked to show its window");
            return false;
        }
    }

    /// <summary>
    /// The app is closing: a second start is no longer asked to wait for this window but waits for the lock (it is held until
    /// <see cref="Dispose"/>, after the exit work).
    /// </summary>
    public void StopListening()
    {
        if (Volatile.Read(ref _disposed) == 0)
        {
            _stop.Cancel();
        }
    }

    /// <summary>
    /// Takes the lock once the instance holding it has ended, trying every half second for at most <paramref name="wait"/>; the result is
    /// first when it was freed in time.
    /// </summary>
    public static SingleInstance WaitForClosing(string dataDirectory, TimeSpan wait)
    {
        var until = DateTime.UtcNow + wait;
        while (true)
        {
            var instance = Acquire(dataDirectory);
            if (instance.IsFirst || DateTime.UtcNow >= until)
            {
                return instance;
            }

            instance.Dispose();
            Thread.Sleep(500);
        }
    }

    /// <inheritdoc />
    /// <remarks>Once only: a canceled start frees the lock before the process ends (<see cref="Startup.StartupExit"/>).</remarks>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _stop.Cancel();
        _lock?.Dispose();
        _stop.Dispose();
    }

    private async Task ListenAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(_name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                var buffer = new byte[16];
                _ = await server.ReadAsync(buffer, _stop.Token).ConfigureAwait(false);
                ShowRequested?.Invoke(this, EventArgs.Empty);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException ex)
            {
                Logger.LogWarning(ex, "The pipe that shows the window for a second start failed; it listens again in a second");
                await Task.Delay(1000).ConfigureAwait(false);
            }
        }
    }
}
