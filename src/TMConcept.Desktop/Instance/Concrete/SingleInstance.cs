using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace TMConcept.Desktop.Instance;

/// <summary>
/// One running app per data folder. The first start holds <c>instance.lock</c> in the data folder open without sharing for its whole life
/// (Windows: no second open; Linux: .NET takes an exclusive flock). A file lock is used, not a named mutex: on Linux .NET scopes a named
/// mutex to the login session, so a start from a terminal and one from the menu both counted as first, and the second one's start-up
/// clean-up then ended the first one's render processes and emptied its work folder. A named pipe (a Unix socket on Linux) carries "show
/// your window" from a second start to the first.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    /// <summary>The lock file's name in the data folder.</summary>
    public const string LockFileName = "instance.lock";

    private readonly FileStream? _lock;
    private readonly string _name;
    private readonly CancellationTokenSource _stop = new();

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

    /// <summary>Asks the first instance to show its window.</summary>
    public void SignalFirst()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", _name, PipeDirection.Out);
            client.Connect(2000);
            client.Write("show\n"u8);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException)
        {
            // The first instance is closing or not listening yet; nothing else to do for a second start.
            Console.Error.WriteLine("TM Concept is already running: " + ex.Message);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
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
                Console.Error.WriteLine("Single instance pipe error: " + ex.Message);
                await Task.Delay(1000).ConfigureAwait(false);
            }
        }
    }
}
