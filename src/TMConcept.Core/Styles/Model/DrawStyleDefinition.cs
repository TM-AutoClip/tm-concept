namespace TMConcept.Core.Styles;

/// <summary>
/// Everything one <see cref="DrawStyle"/> means, in one place (<see cref="DrawStyles"/>; 0.1.32): the token the code reads
/// (<paramref name="Key"/>), the image model's style words (<paramref name="ImagePrompt"/>), how the video code draws a scene without an image
/// (<paramref name="Drawing"/>), its material and motion rules (<paramref name="Craft"/>), the texture the app lays over the picture, a matching
/// built-in palette (<paramref name="PaletteId"/>, empty = none) and, for a storytelling style, how the scenes are adapted to it
/// (<see cref="SceneGuidance"/>, empty = nothing). <paramref name="PrefersImages"/>: the look needs an image model (code alone draws
/// it only roughly). <paramref name="Transition"/> (F1, 0.1.42): the family of scene transitions the look uses in the app's drawer
/// (<see cref="Scenes.SceneTransitions"/>: paper slides, a drafting board or a pencil wipes like an eraser, a terminal cuts; empty = the
/// tempo's own). F12 (0.1.43): <see cref="Pitfalls"/>, the known mistakes of the look that go into the scene plan prompt, and
/// <see cref="Finish"/>, the finishing layer the app's drawer lays over it. The prompts are English (models write code best in English).
/// </summary>
public sealed record DrawStyleDefinition(DrawStyle Style, string Key, string ImagePrompt, string Drawing, string Craft, StyleTexture Texture,
    string PaletteId, bool PrefersImages = false, string Transition = "")
{
    /// <summary>
    /// The look's known mistakes (F12, 0.1.43; two to four short English sentences, empty for <see cref="DrawStyle.Auto"/>): the scene plan
    /// prompt lists them for the chosen style so the same mistake is not made again. No call is added.
    /// </summary>
    public IReadOnlyList<string> Pitfalls { get; init; } = [];

    /// <summary>
    /// The finishing layer of the look (F12, 0.1.43; <see cref="StyleFinish.None"/> for <see cref="DrawStyle.Auto"/> and for pixel art, whose
    /// look is its own buffer and would only be blurred by one).
    /// </summary>
    public StyleFinish Finish { get; init; } = StyleFinish.None;

    /// <summary>
    /// Several transition families of the look (F13, 0.1.51; pixel art: dissolve, iris and blocks, in order of preference); empty = the single
    /// <see cref="Transition"/> (or none). Read through <see cref="Families"/>.
    /// </summary>
    public IReadOnlyList<string> Transitions { get; init; } = [];

    /// <summary>Every transition family of the look, in order of preference (<see cref="Transitions"/>, else <see cref="Transition"/>; empty = none).</summary>
    public IReadOnlyList<string> Families => Transitions.Count > 0 ? Transitions : Scenes.TransitionCatalog.One(Transition);

    /// <summary>
    /// How the scenes are adapted to a storytelling look (28 Nov 2026; English, empty for a plain look): the scene plan and the video code are
    /// told to turn the concept's lines into this look's scenes (the stick-figure narrator acts every line out). The concept itself is written
    /// for no style; the look is chosen on the video page per profile (<see cref="VideoLook"/>), so the adaptation happens while the video is made.
    /// </summary>
    public string SceneGuidance { get; init; } = string.Empty;
}
