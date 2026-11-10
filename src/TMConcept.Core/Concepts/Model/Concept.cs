namespace TMConcept.Core.Concepts;

/// <summary>A video concept: title, two-sentence summary and the voice-over script with <c>[on screen: …]</c> scene lines.</summary>
public sealed class Concept
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Profile (channel).</summary>
    public Guid ProfileId { get; set; }

    /// <summary>Number inside the profile (increasing, never reused).</summary>
    public int Number { get; set; }

    /// <summary>Status.</summary>
    public ConceptStatus Status { get; set; } = ConceptStatus.New;

    /// <summary>Title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Two-sentence summary (from the model).</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Voice-over script; scene lines in square brackets.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>Content language.</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>The generation run that produced it.</summary>
    public Guid? RunId { get; set; }

    /// <summary>The most similar earlier concept (when the duplicate check found one).</summary>
    public Guid? SimilarToId { get; set; }

    /// <summary>Similarity to <see cref="SimilarToId"/> (0..1).</summary>
    public double? Similarity { get; set; }

    /// <summary>Short note of the duplicate check ("similar to #212 (71 %)").</summary>
    public string DuplicateNote { get; set; } = string.Empty;

    /// <summary>Created (UTC).</summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When it was approved or rejected (UTC).</summary>
    public DateTimeOffset? DecidedAtUtc { get; set; }

    /// <summary>Why it is in the trash (<see cref="TrashReason.None"/> when it is not, or no reason was given).</summary>
    public TrashReason TrashReason { get; set; }
}
