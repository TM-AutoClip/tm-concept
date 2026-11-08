using TMConcept.Core.Profiles;

namespace TMConcept.Core.Concepts;

/// <summary>The chosen focus drop-downs as the prompt's FOCUS line, in the prompt's language (Turkish for a Turkish video, English for every other; "Tone: mysterious; Audience: enthusiasts").</summary>
public static class FocusText
{
    /// <summary>The line's text; empty when nothing is chosen.</summary>
    public static string Build(string language, IEnumerable<(FocusDimension Dimension, FocusOption Option)> chosen)
    {
        ArgumentNullException.ThrowIfNull(chosen);
        var turkish = OutputLanguages.Of(language).PromptLanguage == ContentLanguages.Turkish;
        return string.Join("; ", chosen.Select(c => (turkish ? c.Dimension.Name.Tr : c.Dimension.Name.En) + ": " + (turkish ? c.Option.Name.Tr : c.Option.Name.En)));
    }
}
