namespace TMConcept.Core.Videos;

/// <summary>
/// An entry of the production queue ("to be produced"). Removed when produced or taken out of the queue. It keeps no settings: a queued
/// video is made with the choices of the moment it starts (the profile's models, the subtitles checkbox). Until 0.1.16 it kept the
/// subtitles choice of the moment it was added, so turning subtitles off did not reach videos already queued.
/// </summary>
public sealed class QueueItem
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Profile.</summary>
    public Guid ProfileId { get; set; }

    /// <summary>Concept.</summary>
    public Guid ConceptId { get; set; }

    /// <summary>Order (0 = first).</summary>
    public int Position { get; set; }

    /// <summary>Added (UTC).</summary>
    public DateTimeOffset AddedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Failure code of the last try (<see cref="VideoException.Code"/>; empty = it has not failed).</summary>
    public string LastErrorCode { get; set; } = string.Empty;

    /// <summary>Detail of the last failure (the pipeline's English message; the interface shows its own text for the code).</summary>
    public string LastErrorDetail { get; set; } = string.Empty;

    /// <summary>What the failed tries of this item paid together (USD): money lost because of the failures.</summary>
    public decimal FailedUsd { get; set; }
}
