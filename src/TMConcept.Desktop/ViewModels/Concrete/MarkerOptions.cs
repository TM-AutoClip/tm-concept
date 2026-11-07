using TMConcept.Core.Markers;
using TMConcept.Desktop.Localization;

namespace TMConcept.Desktop.ViewModels;

/// <summary>The drop-down entries of the markers of one kind, in the interface language (the token inserted is that language's spelling).</summary>
public static class MarkerOptions
{
    /// <summary>The entries.</summary>
    public static IReadOnlyList<MarkerOption> For(MarkerKind kind, Localizer text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var language = text.Language;
        return MarkerCatalog.Of(kind).Select(m => new MarkerOption(m.TokenOf(language),
            m.TokenOf(language) + " — " + (language == Core.Profiles.ContentLanguages.Turkish ? m.Hint.Tr : m.Hint.En))).ToList();
    }
}
