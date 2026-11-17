namespace TMConcept.Core.Models;

/// <summary>One step of a memory check or placement for the user's activity panel (<see cref="IMemoryNotices"/>; no screen text is made in Core).</summary>
/// <param name="Kind">What happened.</param>
/// <param name="ModelName">The model's display name.</param>
/// <param name="ModelKey">The model's key (provider/id), to tell repeated lines apart.</param>
public sealed record MemoryNotice(MemoryNoticeKind Kind, string ModelName, string ModelKey)
{
    /// <summary>The reading's number (<see cref="MemoryNoticeKind.Measuring"/>), from 1.</summary>
    public int Step { get; init; }

    /// <summary>Of how many readings before the user is asked.</summary>
    public int Of { get; init; }

    /// <summary>How long the wait is (<see cref="MemoryNoticeKind.Settling"/>, <see cref="MemoryNoticeKind.Retrying"/>).</summary>
    public TimeSpan Wait { get; init; }

    /// <summary>Where the weights went (<see cref="MemoryNoticeKind.Placed"/>).</summary>
    public MemoryPlacement Placement { get; init; }

    /// <summary>The figures (<see cref="MemoryNoticeKind.Asking"/>, <see cref="MemoryNoticeKind.Failed"/>, <see cref="MemoryNoticeKind.Canceled"/>).</summary>
    public MemoryShortfall? Shortfall { get; init; }
}
