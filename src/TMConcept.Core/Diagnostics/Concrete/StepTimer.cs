using Microsoft.Extensions.Logging;

namespace TMConcept.Core.Diagnostics;

/// <summary>
/// The step timer (<see cref="IStepTimer"/>). <see cref="Process"/> is the app's own: created when <c>Main</c> starts, before any service
/// exists, so the first steps of the start are measured too; the database layer counts its connections on it. Before the log is attached
/// at most <see cref="PendingMax"/> lines are kept (a process that never attaches one, such as a test run, does not grow). Thread-safe.
/// </summary>
public sealed class StepTimer(TimeProvider time) : IStepTimer
{
    /// <summary>Lines kept until the log is attached; later ones are dropped.</summary>
    public const int PendingMax = 200;

    /// <summary>Name of the log category the lines are written under.</summary>
    public const string Category = "TMConcept.Timing";

    private readonly Lock _lock = new();
    private readonly long _origin = time.GetTimestamp();
    private readonly Dictionary<string, TimeSpan> _totals = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _open = new(StringComparer.Ordinal);
    private readonly List<StepLine> _pending = [];
    private ILogger? _logger;
    private long _connections;
    private long _openingTicks;

    /// <summary>The app's timer (process-wide).</summary>
    public static StepTimer Process { get; } = new(TimeProvider.System);

    /// <inheritdoc />
    public TimeSpan SinceOrigin => ElapsedSince(_origin);

    /// <inheritdoc />
    public ConnectionTotals Connections =>
        new(Interlocked.Read(ref _connections), TimeSpan.FromTicks(Interlocked.Read(ref _openingTicks)));

    /// <inheritdoc />
    public IDisposable Step(string name, bool countConnections = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return new StepScope(this, name, time.GetTimestamp(), countConnections ? Connections : null);
    }

    /// <inheritdoc />
    public void Begin(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        lock (_lock)
        {
            _open[name] = time.GetTimestamp();
        }
    }

    /// <inheritdoc />
    public void End(string name)
    {
        long start;
        lock (_lock)
        {
            if (!_open.Remove(name, out start))
            {
                return;
            }
        }

        Record(name, ElapsedSince(start));
    }

    /// <inheritdoc />
    public void Mark(string name) => Record(name, SinceOrigin);

    /// <inheritdoc />
    public void CountConnection(TimeSpan opening)
    {
        Interlocked.Increment(ref _connections);
        Interlocked.Add(ref _openingTicks, opening.Ticks);
    }

    /// <inheritdoc />
    public void Record(string name, TimeSpan elapsed, ConnectionTotals? connections = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var line = new StepLine(name, (long)Math.Round(elapsed.TotalMilliseconds), connections);
        lock (_lock)
        {
            _totals[name] = _totals.GetValueOrDefault(name) + elapsed;
            if (_logger is { } logger)
            {
                Write(logger, line);
            }
            else if (_pending.Count < PendingMax)
            {
                _pending.Add(line);
            }
        }
    }

    /// <inheritdoc />
    public TimeSpan Total(string name)
    {
        lock (_lock)
        {
            return _totals.GetValueOrDefault(name);
        }
    }

    /// <inheritdoc />
    public void Attach(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        lock (_lock)
        {
            _logger = logger;
            foreach (var line in _pending)
            {
                Write(logger, line);
            }

            _pending.Clear();
        }
    }

    /// <summary>Time from <paramref name="start"/> (a timestamp of this timer's clock) until now.</summary>
    internal TimeSpan ElapsedSince(long start) => time.GetElapsedTime(start);

    private static void Write(ILogger logger, StepLine line)
    {
        if (line.Connections is { } connections)
        {
            logger.LogInformation("{Step} {Milliseconds} (database: {Connections} connection(s) opened in {OpeningMilliseconds} ms)", line.Name,
                line.Milliseconds, connections.Count, (long)Math.Round(connections.Opening.TotalMilliseconds));
        }
        else
        {
            logger.LogInformation("{Step} {Milliseconds}", line.Name, line.Milliseconds);
        }
    }
}
