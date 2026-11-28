using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using TMConcept.Core.Styles;
using TMConcept.Desktop.Localization;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// The profile's (the series') look on the video page, next to the video code model (28 Nov 2026, the developer: "şablondan kaldıralım dedim
/// … bu video üretim seçimi gibi bir şey bunu video kodu ile ayarlamalı"): the drawing style ("Yapay zekâ seçsin" by default, each with its
/// description), how much things move and the style's finishing layer. They left the template; every change is saved for the profile
/// (<see cref="Changed"/>; the page writes it) and each video takes the look when it starts (<see cref="ToLook"/>). With the chosen template
/// (<see cref="ShowTemplate"/>) it says which palette the style suggests and that the finishing layer needs the app's drawer.
/// </summary>
public sealed partial class VideoLookViewModel : ObservableObject
{
    private readonly Localizer _text;
    private VideoStyle? _template;
    private bool _filling;

    /// <summary>Creates the form on the default look (<see cref="Load"/> fills it).</summary>
    public VideoLookViewModel(Localizer text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _text = text;

        // In the catalog's order (the AI choice first), each with its description under the list.
        DrawStyles = Core.Styles.DrawStyles.All.Select(d => new NumberChoice((int)d.Style, Label("style.draw." + Key(d.Style), d.Key))).ToList();
        Motions = Enum.GetValues<MotionIntensity>()
            .Select(m => new NumberChoice(Convert.ToInt32(m, CultureInfo.InvariantCulture), Label("style.motion." + Key(m), m.ToString()))).ToList();
        var look = new VideoLook();
        _filling = true;
        DrawStyle = Pick(DrawStyles, (int)look.DrawStyle);
        Motion = Pick(Motions, (int)look.Motion);
        Finish = look.Finish;
        _filling = false;
        Describe();
    }

    /// <summary>A change worth saving (not while the form is filled).</summary>
    public event EventHandler? Changed;

    /// <summary>The drawing styles (scenes without images; also the style of generated images), the AI choice first.</summary>
    public IReadOnlyList<NumberChoice> DrawStyles { get; }

    /// <summary>Motion intensities.</summary>
    public IReadOnlyList<NumberChoice> Motions { get; }

    /// <summary>The drawing style (null only while the list writes an empty selection back: ignored, nothing is saved).</summary>
    [ObservableProperty]
    public partial NumberChoice? DrawStyle { get; set; }

    /// <summary>What the chosen drawing style looks like (shown under the list; empty for none).</summary>
    [ObservableProperty]
    public partial string DrawStyleNote { get; set; } = string.Empty;

    /// <summary>"Suggested palette for this style: Blueprint (the template's palette: Night)"; empty when none, or the template already has it.</summary>
    [ObservableProperty]
    public partial string SuggestedPaletteText { get; set; } = string.Empty;

    /// <summary>How much things move (null only while the list writes an empty selection back: ignored, nothing is saved).</summary>
    [ObservableProperty]
    public partial NumberChoice? Motion { get; set; }

    /// <summary>The style's finishing layer (paper tooth, grain, vignette, glow, shadow, boil; the app's drawer only). On by default.</summary>
    [ObservableProperty]
    public partial bool Finish { get; set; }

    /// <summary>The chosen template lets the model write free code, so the finishing layer does nothing (a note under the box; empty otherwise).</summary>
    [ObservableProperty]
    public partial string FinishNote { get; set; } = string.Empty;

    /// <summary>Fills the form from <paramref name="look"/> (a profile's choice; nothing is saved by the filling).</summary>
    public void Load(VideoLook look)
    {
        ArgumentNullException.ThrowIfNull(look);
        _filling = true;
        try
        {
            DrawStyle = Pick(DrawStyles, (int)look.DrawStyle);
            Motion = Pick(Motions, (int)look.Motion);
            Finish = look.Finish;
        }
        finally
        {
            _filling = false;
        }

        Describe();
    }

    /// <summary>The chosen template (null = none): its palette and its drawer decide the notes; nothing is saved.</summary>
    public void ShowTemplate(VideoStyle? template)
    {
        _template = template;
        Describe();
    }

    /// <summary>The look as the form says.</summary>
    public VideoLook ToLook() => new()
    {
        DrawStyle = (Core.Styles.DrawStyle)(DrawStyle?.Value ?? (int)Core.Styles.DrawStyle.Auto),
        Motion = (MotionIntensity)(Motion?.Value ?? (int)MotionIntensity.Normal),
        Finish = Finish,
    };

    partial void OnDrawStyleChanged(NumberChoice? value)
    {
        if (value is not null)
        {
            Edited();
        }
    }

    partial void OnMotionChanged(NumberChoice? value)
    {
        if (value is not null)
        {
            Edited();
        }
    }

    partial void OnFinishChanged(bool value) => Edited();

    private void Edited()
    {
        if (_filling)
        {
            return;
        }

        Describe();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The chosen style's description, the palette it suggests next to the template's, and the drawer note.</summary>
    private void Describe()
    {
        var definition = Core.Styles.DrawStyles.Of((Core.Styles.DrawStyle)(DrawStyle?.Value ?? (int)Core.Styles.DrawStyle.Auto));
        var key = "style.draw." + Key(definition.Style) + ".desc";
        var note = _text.Has(key) ? _text[key] : string.Empty;
        DrawStyleNote = definition.PrefersImages && note.Length > 0 ? note + " " + _text["styles.draw.prefers_images"] : note;

        // The palette is the template's (colours are the channel's identity): the suggestion is only information here.
        var palette = VideoStyleRules.Palette(_template);
        SuggestedPaletteText = _template is null || definition.PaletteId.Length == 0 || VideoStyleRules.FindPalette(definition.PaletteId) is null
            || (_template.PaletteId != VideoPalettes.CustomId && palette.Id == definition.PaletteId)
            ? string.Empty
            : _text.Format("look.palette", PaletteName(definition.PaletteId), TemplatePaletteName(_template));
        FinishNote = Finish && _template is { Drawer: not SceneDrawer.App } ? _text["look.finish.drawer_note"] : string.Empty;
    }

    private string PaletteName(string id) => Label("style.palette." + id, id);

    private string TemplatePaletteName(VideoStyle template) => template.PaletteId == VideoPalettes.CustomId
        ? template.CustomPaletteName.Length > 0 ? template.CustomPaletteName : _text["styles.own"]
        : PaletteName(VideoStyleRules.Palette(template).Id);

    private string Label(string key, string fallback) => _text.Has(key) ? _text[key] : fallback;

    private static string Key<T>(T value)
        where T : struct, Enum => value.ToString().ToLowerInvariant();

    private static NumberChoice Pick(IReadOnlyList<NumberChoice> choices, int value) => choices.FirstOrDefault(c => c.Value == value) ?? choices[0];
}
