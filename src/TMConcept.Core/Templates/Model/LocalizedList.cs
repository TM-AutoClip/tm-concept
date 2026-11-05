namespace TMConcept.Core.Templates;

/// <summary>A list of texts in every content language.</summary>
public sealed class LocalizedList
{
    /// <summary>Turkish.</summary>
    public List<string> Tr { get; set; } = [];

    /// <summary>English.</summary>
    public List<string> En { get; set; } = [];
}
