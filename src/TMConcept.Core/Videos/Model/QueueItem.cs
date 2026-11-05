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
}
