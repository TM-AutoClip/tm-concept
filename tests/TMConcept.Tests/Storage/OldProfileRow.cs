namespace TMConcept.Tests.Storage;

/// <summary>A row of <c>profiles</c> as 0.1.18 stored it (no value converters, no ChannelPhoto column).</summary>
internal sealed class OldProfileRow
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string ContentLanguage { get; set; } = string.Empty;

    public int Format { get; set; }

    public int TargetSeconds { get; set; }

    public string ConceptText { get; set; } = string.Empty;

    public string? TemplateId { get; set; }

    public int ConceptCounter { get; set; }

    public long CreatedAtUtc { get; set; }

    public long UpdatedAtUtc { get; set; }
}
