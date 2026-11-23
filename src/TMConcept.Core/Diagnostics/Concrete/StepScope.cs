namespace TMConcept.Core.Diagnostics;

/// <summary>A running step of <see cref="StepTimer.Step"/>: disposing it (once) writes the step's line.</summary>
public sealed class StepScope : IDisposable
{
    private readonly StepTimer _owner;
    private readonly string _name;
    private readonly long _start;
    private readonly ConnectionTotals? _connectionsBefore;
    private int _disposed;

    internal StepScope(StepTimer owner, string name, long start, ConnectionTotals? connectionsBefore)
    {
        _owner = owner;
        _name = name;
        _start = start;
        _connectionsBefore = connectionsBefore;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _owner.Record(_name, _owner.ElapsedSince(_start), _connectionsBefore is { } before ? _owner.Connections - before : null);
        }
    }
}
