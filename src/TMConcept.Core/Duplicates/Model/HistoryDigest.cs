namespace TMConcept.Core.Duplicates;

/// <summary>The compressed history of a profile for the prompt (older titles grouped once the history is long).</summary>
public sealed class HistoryDigest
{
    /// <summary>Profile.</summary>
    public Guid ProfileId { get; set; }

    /// <summary>Version (increased on every update).</summary>
    public int Version { get; set; }

    /// <summary>Grouped text of the older titles.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Concepts up to this number are covered.</summary>
    public int CoversUntilNumber { get; set; }

    /// <summary>Updated (UTC).</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
