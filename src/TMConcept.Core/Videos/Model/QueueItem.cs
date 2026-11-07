namespace TMConcept.Core.Videos;

/// <summary>An entry of the production queue ("to be produced"). Removed when produced or taken out of the queue.</summary>
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

    /// <summary>Production settings as JSON.</summary>
    public string SettingsJson { get; set; } = "{}";

    /// <summary>Added (UTC).</summary>
    public DateTimeOffset AddedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Failure code of the last try (<see cref="VideoException.Code"/>; empty = it has not failed).</summary>
    public string LastErrorCode { get; set; } = string.Empty;

    /// <summary>Detail of the last failure (the pipeline's English message; the interface shows its own text for the code).</summary>
    public string LastErrorDetail { get; set; } = string.Empty;

    /// <summary>What the failed tries of this item paid together (USD): money lost because of the failures.</summary>
    public decimal FailedUsd { get; set; }
}
