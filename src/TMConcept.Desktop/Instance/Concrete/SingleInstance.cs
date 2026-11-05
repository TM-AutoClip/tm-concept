using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace TMConcept.Desktop.Instance;

/// <summary>
/// One running app per data folder: a named mutex decides, a named pipe (a Unix socket on Linux) carries "show your window" from a second
/// start to the first.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _name;
    private readonly CancellationTokenSource _stop = new();

    private SingleInstance(Mutex mutex, string name, bool first)
    {
        _mutex = mutex;
        _name = name;
        IsFirst = first;
        if (first)
        {
            _ = ListenAsync();
        }
    }

    /// <summary>Raised (on a pool thread) when a second start asks for the window.</summary>
    public event EventHandler? ShowRequested;

    /// <summary>Whether this is the first instance.</summary>
    public bool IsFirst { get; }

    /// <summary>Takes the instance lock of the data folder.</summary>
    public static SingleInstance Acquire(string dataDirectory)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(dataDirectory).ToUpperInvariant())))[..16];
        var name = "TMConcept-" + hash;
        var mutex = new Mutex(true, name, out var created);
        return new SingleInstance(mutex, name, created);
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
        if (IsFirst)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
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
