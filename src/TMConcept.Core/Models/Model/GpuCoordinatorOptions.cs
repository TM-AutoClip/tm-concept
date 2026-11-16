namespace TMConcept.Core.Models;

/// <summary>Timing of <see cref="GpuCoordinator"/>'s wait for an unloaded model's memory.</summary>
/// <param name="PollInterval">Time between two memory readings (drivers free memory lazily).</param>
/// <param name="RecoveryTimeout">Longest wait for the memory; the lease is granted after it anyway (the step then moves weights to main memory).</param>
public sealed record GpuCoordinatorOptions(TimeSpan PollInterval, TimeSpan RecoveryTimeout)
{
    /// <summary>300 ms between readings, at most 10 seconds.</summary>
    public static GpuCoordinatorOptions Default { get; } = new(TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(10));
}