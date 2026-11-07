using TMConcept.Core.Profiles;
using TMConcept.Desktop.Localization;

namespace TMConcept.Desktop.ViewModels;

/// <summary>The output languages as picker options: the language's own name with its name in the interface language ("Deutsch — Almanca").</summary>
public static class LanguageOptions
{
    /// <summary>Every output language in the order of <see cref="ContentLanguages.Definitions"/>.</summary>
    public static IReadOnlyList<ChoiceOption> For(Localizer text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return ContentLanguages.Definitions
            .Select(d => new ChoiceOption(d.Code, OutputLanguages.NameIn(d.Code, text.Language) is var name && name != d.Native ? d.Native + " — " + name : d.Native))
            .ToList();
    }

    /// <summary>The output language of the interface language (Turkish or English), for a page that has no profile.</summary>
    public static string OfInterface(Localizer text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Language == ContentLanguages.Turkish ? ContentLanguages.Turkish : ContentLanguages.English;
    }
}
