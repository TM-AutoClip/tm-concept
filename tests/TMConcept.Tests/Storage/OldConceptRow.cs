using TMConcept.Core.Concepts;

namespace TMConcept.Tests.Storage;

/// <summary>A row of <c>concepts</c> as 0.1.17–0.1.20 stored it (no value converters, no Sources column of 0.1.21).</summary>
internal sealed class OldConceptRow
{
    public string Id { get; set; } = string.Empty;

    public string ProfileId { get; set; } = string.Empty;

    public int Number { get; set; }

    public int Status { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string Language { get; set; } = string.Empty;

    public string? RunId { get; set; }

    public string? SimilarToId { get; set; }

    public double? Similarity { get; set; }

    public string DuplicateNote { get; set; } = string.Empty;

    public long CreatedAtUtc { get; set; }

    public long? DecidedAtUtc { get; set; }

    public int TrashReason { get; set; }

    /// <summary>The row of <paramref name="concept"/> with the number <paramref name="number"/>.</summary>
    public static OldConceptRow Of(Concept concept, int number) => new()
    {
        Id = concept.Id.ToString("D").ToUpperInvariant(),
        ProfileId = concept.ProfileId.ToString("D").ToUpperInvariant(),
        Number = number,
        Status = (int)concept.Status,
        Title = concept.Title,
        Summary = concept.Summary,
        Content = concept.Content,
        Language = concept.Language,
        DuplicateNote = concept.DuplicateNote,
        CreatedAtUtc = concept.CreatedAtUtc.UtcTicks,
    };
}
