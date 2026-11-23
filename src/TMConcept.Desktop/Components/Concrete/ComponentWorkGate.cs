using Microsoft.Extensions.Logging;
using TMConcept.Desktop.Paths;

namespace TMConcept.Desktop.Components;

/// <inheritdoc cref="IComponentWorkGate" />
public sealed class ComponentWorkGate(AppPaths paths, ILogger<ComponentWorkGate> logger) : IComponentWorkGate
{
    /// <summary>The end of the marker file's name (<c>tools.reinstalling</c>, a file: the storage page lists only folders next to <c>tools</c>).</summary>
    public const string MarkerSuffix = ".reinstalling";

    private readonly Lock _sync = new();
    private int _installs;
    private int _reinstalls;

    /// <inheritdoc />
    public string MarkerFile => Path.TrimEndingDirectorySeparator(paths.ToolsDirectory) + MarkerSuffix;

    /// <inheritdoc />
    public bool Busy
    {
        get
        {
            lock (_sync)
            {
                return _installs > 0 || _reinstalls > 0;
            }
        }
    }

    /// <inheritdoc />
    public bool Reinstalling
    {
        get
        {
            lock (_sync)
            {
                return _reinstalls > 0;
            }
        }
    }

    /// <inheritdoc />
    public bool Interrupted => !Reinstalling && File.Exists(MarkerFile);

    /// <inheritdoc />
    public IDisposable Install()
    {
        lock (_sync)
        {
            _installs++;
        }

        return new Lease(() =>
        {
            lock (_sync)
            {
                _installs--;
            }
        });
    }

    /// <inheritdoc />
    public IDisposable Reinstall()
    {
        lock (_sync)
        {
            if (_reinstalls++ == 0)
            {
                Write();
            }
        }

        return new Lease(() =>
        {
            lock (_sync)
            {
                if (--_reinstalls == 0)
                {
                    Delete();
                }
            }
        });
    }

    /// <inheritdoc />
    public void ForgetInterrupted()
    {
        lock (_sync)
        {
            if (_reinstalls == 0)
            {
                Delete();
            }
        }
    }

    private void Write()
    {
        try
        {
            File.WriteAllText(MarkerFile, DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Without the marker a crash in the middle is not known at the next start; the reinstall itself goes on.
            logger.LogWarning(ex, "The reinstall marker could not be written");
        }
    }

    private void Delete()
    {
        try
        {
            File.Delete(MarkerFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "The reinstall marker could not be deleted");
        }
    }

    /// <summary>Gives the gate back once.</summary>
    private sealed class Lease(Action release) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                release();
            }
        }
    }
}
