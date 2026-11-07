using System.Globalization;

namespace TMConcept.Core.Profiles;

/// <summary>Looks up an output language (<see cref="ContentLanguages.Definitions"/>); an unknown code gets English rates and English prompts.</summary>
public static class OutputLanguages
{
    /// <summary>The language of <paramref name="code"/>; an unknown code (a damaged profile) gets the English rates under its own code.</summary>
    public static OutputLanguage Of(string? code)
    {
        var found = ContentLanguages.Definitions.FirstOrDefault(d => string.Equals(d.Code, code, StringComparison.Ordinal));
        if (found is not null)
        {
            return found;
        }

        var english = ContentLanguages.Definitions.First(d => d.Code == ContentLanguages.English);
        var name = string.IsNullOrWhiteSpace(code) ? english.English : code;
        return english with { Code = Known(code) ? code! : ContentLanguages.English, Native = name, Turkish = name, English = name };
    }

    /// <summary>Whether a code is one of the output languages.</summary>
    public static bool IsKnown(string? code) => ContentLanguages.All.Contains(code ?? string.Empty, StringComparer.Ordinal);

    /// <summary>The name of a language in the interface language (<paramref name="interfaceLanguage"/> "tr" or "en").</summary>
    public static string NameIn(string? code, string interfaceLanguage)
    {
        var language = Of(code);
        return interfaceLanguage == ContentLanguages.Turkish ? language.Turkish : language.English;
    }

    /// <summary>Whether <paramref name="code"/> names a culture this system knows (so <see cref="OutputLanguage.Culture"/> can be built).</summary>
    private static bool Known(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 8 || !code.All(char.IsAsciiLetterLower))
        {
            return false;
        }

        try
        {
            return CultureInfo.GetCultureInfo(code).Name.Length > 0;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }
}
