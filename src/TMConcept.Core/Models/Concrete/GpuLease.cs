using TMConcept.Core.Hardware;

namespace TMConcept.Core.Models;

/// <summary>A lease of <see cref="GpuCoordinator"/>; disposing it more than once does nothing.</summary>
public sealed class GpuLease : IGpuLease
{
    private readonly Func<GpuLease, ValueTask>? _release;
    private int _disposed;

    /// <summary>A lease; <paramref name="release"/> runs once when it is disposed (null = nothing to give back).</summary>
    public GpuLease(IModel model, bool onCard, VramReading memory, Func<GpuLease, ValueTask>? release)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(memory);
        Model = model;
        OnCard = onCard;
        Memory = memory;
        _release = release;
    }

    /// <inheritdoc />
    public IModel Model { get; }

    /// <inheritdoc />
    public bool OnCard { get; }

    /// <inheritdoc />
    public VramReading Memory { get; }

    /// <summary>Whether it was disposed.</summary>
    public bool Disposed => Volatile.Read(ref _disposed) != 0;

    /// <inheritdoc />
    public ValueTask DisposeAsync() => Interlocked.Exchange(ref _disposed, 1) == 0 && _release is not null ? _release(this) : ValueTask.CompletedTask;
}