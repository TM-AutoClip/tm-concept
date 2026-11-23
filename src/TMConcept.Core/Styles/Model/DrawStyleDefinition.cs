using TMConcept.Core.Templates;

namespace TMConcept.Core.Styles;

/// <summary>
/// Everything one <see cref="DrawStyle"/> means, in one place (<see cref="DrawStyles"/>; 0.1.32): the token the code reads
/// (<paramref name="Key"/>), the image model's style words (<paramref name="ImagePrompt"/>), how the video code draws a scene without an image
/// (<paramref name="Drawing"/>), its material and motion rules (<paramref name="Craft"/>), the texture the app lays over the picture, a matching
/// built-in palette (<paramref name="PaletteId"/>, empty = none) and, for a storytelling style, what the concept must be written for
/// (<paramref name="ConceptGuidance"/>, empty = nothing). <paramref name="PrefersImages"/>: the look needs an image model (code alone draws
/// it only roughly). The prompts are English (models write code best in English); the guidance is in the concept prompt's language.
/// </summary>
public sealed record DrawStyleDefinition(DrawStyle Style, string Key, string ImagePrompt, string Drawing, string Craft, StyleTexture Texture,
    string PaletteId, LocalizedText ConceptGuidance, bool PrefersImages = false)
{
    /// <summary>Whether a concept written for this style differs from a plain one.</summary>
    public bool HasConceptGuidance => ConceptGuidance.Tr.Length > 0 || ConceptGuidance.En.Length > 0;

    /// <summary>The concept guidance in a prompt language ("tr" or anything else = English); empty when the style has none.</summary>
    public string GuidanceFor(string language) => language == Profiles.ContentLanguages.Turkish ? ConceptGuidance.Tr : ConceptGuidance.En;
}
