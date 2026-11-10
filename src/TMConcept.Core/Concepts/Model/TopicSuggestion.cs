namespace TMConcept.Core.Concepts;

/// <summary>
/// A topic "Suggest topics" once brought for a profile. Every one is kept (deleted with its profile), so the next press can tell the model
/// what it already suggested and drop a repeat locally (the developer, 10 Nov 2026: "could it ask the same ones again every time?").
/// </summary>
public sealed class TopicSuggestion
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The profile it was suggested for.</summary>
    public Guid ProfileId { get; set; }

    /// <summary>The topic as shown (one line).</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>When it was suggested (UTC).</summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>The user clicked it (it went into the extra request).</summary>
    public bool Used { get; set; }
}
