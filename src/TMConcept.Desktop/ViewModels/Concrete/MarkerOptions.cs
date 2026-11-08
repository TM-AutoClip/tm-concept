using TMConcept.Core.Markers;
using TMConcept.Desktop.Localization;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// The drop-down entries of the placeholders, in the interface language (the token inserted is that language's spelling; both spellings are
/// understood): every one the program applies itself — pauses, speed, transitions.
/// </summary>
public static class MarkerOptions
{
    /// <summary>The entries.</summary>
    public static IReadOnlyList<MarkerOption> For(Localizer text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var language = text.Language;
        return MarkerCatalog.All.Select(m => new MarkerOption(m.TokenOf(language), m.HintOf(language))).ToList();
    }
}
