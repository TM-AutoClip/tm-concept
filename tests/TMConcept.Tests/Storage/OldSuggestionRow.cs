namespace TMConcept.Tests.Storage;

/// <summary>A row of <c>topic_suggestions</c> as 0.1.17 stored it (no value converters, no Outcome column).</summary>
internal sealed class OldSuggestionRow
{
    public string Id { get; set; } = string.Empty;

    public string ProfileId { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public long CreatedAtUtc { get; set; }
}
