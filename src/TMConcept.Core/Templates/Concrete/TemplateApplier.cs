using TMConcept.Core.Profiles;

namespace TMConcept.Core.Templates;

/// <summary>Copies a template's texts into a profile in the profile's content language (the template is not changed).</summary>
public static class TemplateApplier
{
    /// <summary>Fills name, concept, format and target length; the speech settings and images of the profile stay.</summary>
    public static void Apply(ChannelTemplate template, Profile profile)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(profile);
        profile.Name = Pick(template.ChannelName, profile.ContentLanguage);
        profile.ConceptText = Pick(template.Concept, profile.ContentLanguage);
        profile.Format = template.Format == "horizontal" ? VideoFormat.Horizontal : VideoFormat.Vertical;
        profile.TargetSeconds = Math.Clamp(template.TargetSeconds, ProfileLimits.SecondsMin, ProfileLimits.SecondsMax);
        profile.TemplateId = template.Id;
    }

    /// <summary>The text in a language (English when the language is unknown).</summary>
    public static string Pick(LocalizedText text, string language)
    {
        ArgumentNullException.ThrowIfNull(text);
        return language == ContentLanguages.Turkish ? text.Tr : text.En;
    }

    /// <summary>The list in a language.</summary>
    public static IReadOnlyList<string> Pick(LocalizedList list, string language)
    {
        ArgumentNullException.ThrowIfNull(list);
        return language == ContentLanguages.Turkish ? list.Tr : list.En;
    }
}
