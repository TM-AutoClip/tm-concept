namespace TMConcept.Core.Templates;

/// <summary>
/// A ready channel template (embedded file <c>Templates/&lt;category&gt;/&lt;id&gt;.json</c>, read-only). "Apply template" copies its texts
/// into a profile; the template itself never changes and a later template change never changes a profile.
/// </summary>
public sealed class ChannelTemplate
{
    /// <summary>Id ("history.byzantine").</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Category name.</summary>
    public LocalizedText Category { get; set; } = new();

    /// <summary>Template name.</summary>
    public LocalizedText Name { get; set; } = new();

    /// <summary>Suggested channel name.</summary>
    public LocalizedText ChannelName { get; set; } = new();

    /// <summary>Channel concept (vision, goal, what the videos are like, tone, avoid).</summary>
    public LocalizedText Concept { get; set; } = new();

    /// <summary>"vertical" or "horizontal".</summary>
    public string Format { get; set; } = "vertical";

    /// <summary>Target length in seconds.</summary>
    public int TargetSeconds { get; set; } = 75;

    /// <summary>Extra prompt examples.</summary>
    public LocalizedList ExtraPromptExamples { get; set; } = new();
}
