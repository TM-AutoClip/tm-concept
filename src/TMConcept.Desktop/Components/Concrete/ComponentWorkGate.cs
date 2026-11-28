namespace TMConcept.Desktop.Components;

/// <inheritdoc cref="IComponentWorkGate" />
public sealed class ComponentWorkGate : IComponentWorkGate
{
    private int _installs;

    /// <inheritdoc />
    public bool Busy => Volatile.Read(ref _installs) > 0;

    /// <inheritdoc />
    public IDisposable Install()
    {
        Interlocked.Increment(ref _installs);
        return new Lease(() => Interlocked.Decrement(ref _installs));
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
