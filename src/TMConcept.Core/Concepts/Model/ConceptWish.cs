namespace TMConcept.Core.Concepts;

/// <summary>
/// One instruction of an AI edit (✨) of a profile's concept: the user's wish about the telling. The newest few go along with every new
/// concept prompt ("apply from the start"), so the same fix is not asked for again and again (the developer, 10 Nov 2026). Deleted with the
/// profile; a backup holds them with the concepts.
/// </summary>
public sealed class ConceptWish
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The profile it was asked for.</summary>
    public Guid ProfileId { get; set; }

    /// <summary>The instruction as the user wrote it (one line).</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>When it was asked (UTC).</summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
