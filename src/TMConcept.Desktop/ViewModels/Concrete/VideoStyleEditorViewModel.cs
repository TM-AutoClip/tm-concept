using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Layout;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TMConcept.Core.Profiles;
using TMConcept.Core.Speech;
using TMConcept.Core.Styles;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Videos;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// The form of one template in Settings → Templates: every field of <see cref="VideoStyle"/> with a live preview (the palette's colours, the
/// font, the title style and position, the progress bar and subtitles when they are on) and the closing sentence as it would be said in the
/// interface language. Nothing is stored until <see cref="ToStyle"/> is saved by the section.
/// </summary>
public sealed partial class VideoStyleEditorViewModel : ObservableObject
{
    private readonly VideoStyle _source;
    private readonly Localizer _text;
    private bool _filling;

    /// <summary>
    /// Fills the form from <paramref name="source"/> (a copy is edited; the source is not changed); <paramref name="saved"/> are the user's
    /// saved palettes, listed after the shipped ones and before the template's own colours.
    /// </summary>
    public VideoStyleEditorViewModel(VideoStyle source, bool isNew, Localizer text, IReadOnlyList<SavedPalette>? saved = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(text);
        _source = VideoStyleRules.Copy(source);
        _text = text;
        IsNew = isNew;
        Palettes = new ObservableCollection<PaletteOption>(VideoPalettes.All.Select(p => Option(p, Label("style.palette." + p.Id, p.Id))));
        foreach (var palette in saved ?? [])
        {
            Palettes.Add(Option(palette.Colors, palette.Name, palette.Id));
        }

        var own = _source.CustomPalette ?? VideoStyleRules.Palette(_source) with { Id = VideoPalettes.CustomId };
        Palettes.Add(Option(own, text["styles.own"], own: true));
        Fonts = VideoFonts.All.Concat(SystemFonts.Installed).Select(f => new FontOption(f, VideoFontPreview.For(f))).ToList();
        Emphases = Choices<FontEmphasis>("style.emphasis.");
        TitleStyles = Choices<TitleStyle>("style.title.");
        // 0.1.32: in the catalog's order (the AI choice first), each with its description under the list.
        DrawStyles = Core.Styles.DrawStyles.All.Select(d => new NumberChoice((int)d.Style, Label("style.draw." + Key(d.Style), d.Key))).ToList();
        Motions = Choices<MotionIntensity>("style.motion.");
        Tempos = Choices<NarrationTempo>("style.tempo.");
        Drawers = Choices<SceneDrawer>("style.drawer.");
        Placements = Choices<ClosingPlacement>("style.closing.");
        Platforms = Choices<ClosingPlatform>("style.platform.");
        TitleCells = Enum.GetValues<TitlePosition>().Select(p => new TitleCellViewModel(p, Label("style.position." + Key(p), p.ToString()))).ToList();
        foreach (var cell in TitleCells)
        {
            cell.PropertyChanged += OnCellChanged;
        }

        _filling = true;
        Name = _source.Name;
        Subtitles = _source.Subtitles;
        WordHighlight = _source.WordHighlight;
        ProgressBar = _source.ProgressBar;
        ReadableText = _source.ReadableText;
        Font = Fonts.FirstOrDefault(f => f.Font.Id == _source.FontId) ?? Fonts[0];
        Emphasis = Pick(Emphases, (int)_source.FontEmphasis);
        Palette = _source.PaletteId == VideoPalettes.CustomId
            ? Palettes.FirstOrDefault(p => p.SavedId is not null && p.Label == _source.CustomPaletteName && p.Palette == _source.CustomPalette)
                ?? Palettes[^1]
            : Palettes.FirstOrDefault(p => !p.Editable && p.Palette.Id == _source.PaletteId) ?? Palettes[0];
        Fill(Palette.Palette);
        PaletteName = Palette.SavedId is not null ? Palette.Label : string.Empty;
        TitleStyle = Pick(TitleStyles, (int)_source.TitleStyle);
        DrawStyle = Pick(DrawStyles, (int)_source.DrawStyle);
        ShowDrawStyle();
        Motion = Pick(Motions, (int)_source.Motion);
        Tempo = Pick(Tempos, (int)_source.Tempo);
        Drawer = Pick(Drawers, (int)_source.Drawer);
        Closing = Pick(Placements, (int)_source.Closing);
        Platform = Pick(Platforms, (int)_source.ClosingPlatform);
        Phrases = PhraseChoices((ClosingPlatform)Platform.Value);
        Phrase = Pick(Phrases, (int)_source.ClosingPhrase);
        Position = _source.TitlePosition;
        _filling = false;
        Refresh();
    }

    /// <summary>A template not saved yet.</summary>
    public bool IsNew { get; }

    /// <summary>The template's id (a new one keeps it when saved).</summary>
    public Guid Id => _source.Id;

    /// <summary>The form differs from the template it was opened with (the change time aside): unsaved changes.</summary>
    public bool Changed => !VideoStyleRules.SameContent(ToStyle(), _source);

    /// <summary>The colour palettes with their swatches: the shipped ones, the saved ones, then the template's own colours.</summary>
    public ObservableCollection<PaletteOption> Palettes { get; }

    /// <summary>Own colours: background (#RRGGBB).</summary>
    [ObservableProperty]
    public partial string EditBackground { get; set; } = string.Empty;

    /// <summary>Own colours: surface.</summary>
    [ObservableProperty]
    public partial string EditSurface { get; set; } = string.Empty;

    /// <summary>Own colours: accent.</summary>
    [ObservableProperty]
    public partial string EditAccent { get; set; } = string.Empty;

    /// <summary>Own colours: second accent.</summary>
    [ObservableProperty]
    public partial string EditAccent2 { get; set; } = string.Empty;

    /// <summary>Own colours: text.</summary>
    [ObservableProperty]
    public partial string EditText { get; set; } = string.Empty;

    /// <summary>Own colours: quiet text.</summary>
    [ObservableProperty]
    public partial string EditMuted { get; set; } = string.Empty;

    /// <summary>The base colour a harmonious palette is made from.</summary>
    [ObservableProperty]
    public partial string BaseColour { get; set; } = "#2E86DE";

    /// <summary>The generated palette is light (dark text on a light background).</summary>
    [ObservableProperty]
    public partial bool GenerateLight { get; set; }

    /// <summary>The name the own colours are saved under.</summary>
    [ObservableProperty]
    public partial string PaletteName { get; set; } = string.Empty;

    /// <summary>The colours of the form are edited (own colours or a saved palette is chosen).</summary>
    [ObservableProperty]
    public partial bool OwnColours { get; set; }

    /// <summary>The chosen palette is a saved one (it can be deleted).</summary>
    [ObservableProperty]
    public partial bool SavedChosen { get; set; }

    /// <summary>The contrast check of the own colours, in words.</summary>
    [ObservableProperty]
    public partial string PaletteStatus { get; set; } = string.Empty;

    /// <summary>The own colours can be used (no blocking contrast problem).</summary>
    [ObservableProperty]
    public partial bool PaletteOk { get; set; } = true;

    /// <summary>The chosen saved palette (it can be saved again under its name or deleted); <c>null</c> for any other.</summary>
    public Guid? SavedPaletteId => Palette?.SavedId;

    /// <summary>The video fonts, each named in its own font.</summary>
    public IReadOnlyList<FontOption> Fonts { get; }

    /// <summary>Weight and slant of the text (automatic, regular, bold, italic, bold italic).</summary>
    public IReadOnlyList<NumberChoice> Emphases { get; }

    /// <summary>Title styles.</summary>
    public IReadOnlyList<NumberChoice> TitleStyles { get; }

    /// <summary>Drawing styles (scenes without images; also the style of generated images).</summary>
    public IReadOnlyList<NumberChoice> DrawStyles { get; }

    /// <summary>Motion intensities.</summary>
    public IReadOnlyList<NumberChoice> Motions { get; }

    /// <summary>Narration tempos (0.1.31).</summary>
    public IReadOnlyList<NumberChoice> Tempos { get; }

    /// <summary>Who draws the HyperFrames videos (0.1.33): the app from the scene plan, or the model's free code.</summary>
    public IReadOnlyList<NumberChoice> Drawers { get; }

    /// <summary>Where the closing goes (off, voice, screen, both).</summary>
    public IReadOnlyList<NumberChoice> Placements { get; }

    /// <summary>The platform whose wording the closing uses.</summary>
    public IReadOnlyList<NumberChoice> Platforms { get; }

    /// <summary>The ready closing sentences, in the interface language with the chosen platform's wording.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<NumberChoice> Phrases { get; set; }

    /// <summary>The 3×3 title position grid (row by row).</summary>
    public IReadOnlyList<TitleCellViewModel> TitleCells { get; }

    /// <summary>Name.</summary>
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    /// <summary>Subtitles in the picture.</summary>
    [ObservableProperty]
    public partial bool Subtitles { get; set; }

    /// <summary>Progress bar drawn by the app.</summary>
    [ObservableProperty]
    public partial bool ProgressBar { get; set; }

    /// <summary>Readable text: an outline and shadow in the colour opposite to the text (on by default).</summary>
    [ObservableProperty]
    public partial bool ReadableText { get; set; }

    /// <summary>Preview: the outline colour of readable text (opposite to the text colour).</summary>
    [ObservableProperty]
    public partial Color PreviewReadableOutline { get; set; }

    /// <summary>Font.</summary>
    [ObservableProperty]
    public partial FontOption Font { get; set; }

    /// <summary>Colour palette.</summary>
    [ObservableProperty]
    public partial PaletteOption Palette { get; set; }

    /// <summary>Weight and slant of the text.</summary>
    [ObservableProperty]
    public partial NumberChoice Emphasis { get; set; }

    /// <summary>Preview: the weight of the title.</summary>
    [ObservableProperty]
    public partial FontWeight PreviewTitleWeight { get; set; } = FontWeight.Bold;

    /// <summary>Preview: the weight of the subtitle.</summary>
    [ObservableProperty]
    public partial FontWeight PreviewTextWeight { get; set; } = FontWeight.Bold;

    /// <summary>Preview: the slant of every text.</summary>
    [ObservableProperty]
    public partial FontStyle PreviewFontStyle { get; set; } = FontStyle.Normal;

    /// <summary>Title style.</summary>
    [ObservableProperty]
    public partial NumberChoice TitleStyle { get; set; }

    /// <summary>Drawing style.</summary>
    [ObservableProperty]
    public partial NumberChoice DrawStyle { get; set; }

    /// <summary>What the chosen drawing style looks like (0.1.32, shown under the list).</summary>
    [ObservableProperty]
    public partial string DrawStyleNote { get; set; } = string.Empty;

    /// <summary>The built-in palette the chosen style suggests (null = none, or it is already chosen).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSuggestedPalette), nameof(SuggestedPaletteText))]
    public partial PaletteOption? SuggestedPalette { get; set; }

    /// <summary>Whether a palette is suggested.</summary>
    public bool HasSuggestedPalette => SuggestedPalette is not null;

    /// <summary>"Suggested palette for this style: Blueprint".</summary>
    public string SuggestedPaletteText => SuggestedPalette is { } palette ? _text.Format("styles.draw.palette", palette.Label) : string.Empty;

    /// <summary>Takes the palette the style suggests.</summary>
    [RelayCommand]
    public void ApplySuggestedPalette()
    {
        if (SuggestedPalette is { } palette)
        {
            Palette = palette;
        }
    }

    /// <summary>Motion intensity.</summary>
    [ObservableProperty]
    public partial NumberChoice Motion { get; set; }

    /// <summary>Word-by-word highlighted subtitles (0.1.34).</summary>
    [ObservableProperty]
    public partial bool WordHighlight { get; set; }

    /// <summary>Narration tempo: the silences between sentences and scenes.</summary>
    [ObservableProperty]
    public partial NumberChoice Tempo { get; set; }

    /// <summary>Who draws the video (0.1.33).</summary>
    [ObservableProperty]
    public partial NumberChoice Drawer { get; set; }

    /// <summary>Closing placement.</summary>
    [ObservableProperty]
    public partial NumberChoice Closing { get; set; }

    /// <summary>Closing platform wording.</summary>
    [ObservableProperty]
    public partial NumberChoice Platform { get; set; }

    /// <summary>Closing sentence.</summary>
    [ObservableProperty]
    public partial NumberChoice? Phrase { get; set; }

    /// <summary>Title position.</summary>
    [ObservableProperty]
    public partial TitlePosition Position { get; set; }

    /// <summary>The closing has a sentence and a platform to choose (it is not off).</summary>
    [ObservableProperty]
    public partial bool ClosingOn { get; set; }

    /// <summary>Preview: page background.</summary>
    [ObservableProperty]
    public partial IBrush PreviewBackground { get; set; } = Brushes.Black;

    /// <summary>Preview: the title's box (surface for a band, accent for a box, nothing otherwise).</summary>
    [ObservableProperty]
    public partial IBrush PreviewTitleBox { get; set; } = Brushes.Transparent;

    /// <summary>Preview: the title's text colour.</summary>
    [ObservableProperty]
    public partial IBrush PreviewTitleText { get; set; } = Brushes.White;

    /// <summary>Preview: the accent colour (progress bar, underline).</summary>
    [ObservableProperty]
    public partial IBrush PreviewAccent { get; set; } = Brushes.Orange;

    /// <summary>Preview: the text colour (subtitles, outline under the title).</summary>
    [ObservableProperty]
    public partial IBrush PreviewText { get; set; } = Brushes.White;

    /// <summary>Preview: the outline colour (the page background).</summary>
    [ObservableProperty]
    public partial Color PreviewOutline { get; set; }

    /// <summary>Preview: the title is outlined.</summary>
    [ObservableProperty]
    public partial bool PreviewOutlined { get; set; }

    /// <summary>Preview: the title has an accent line under it.</summary>
    [ObservableProperty]
    public partial bool PreviewUnderline { get; set; }

    /// <summary>Preview: the title box spans the width (band).</summary>
    [ObservableProperty]
    public partial HorizontalAlignment PreviewTitleBoxAlignment { get; set; }

    /// <summary>Preview: horizontal place of the title.</summary>
    [ObservableProperty]
    public partial HorizontalAlignment PreviewTitleHorizontal { get; set; }

    /// <summary>Preview: vertical place of the title.</summary>
    [ObservableProperty]
    public partial VerticalAlignment PreviewTitleVertical { get; set; }

    /// <summary>The closing as it would be said and shown in a video in the interface language; empty when off.</summary>
    [ObservableProperty]
    public partial string ClosingPreview { get; set; } = string.Empty;

    /// <summary>The template as the form says (id and creation time of the source; the change time is now).</summary>
    public VideoStyle ToStyle()
    {
        var style = VideoStyleRules.Copy(_source);
        style.Name = Name.Trim();
        style.Subtitles = Subtitles;
        style.ProgressBar = ProgressBar;
        style.ReadableText = ReadableText;
        style.FontId = Font.Font.Id;
        style.FontEmphasis = (FontEmphasis)Emphasis.Value;
        if (Palette.Editable)
        {
            style.PaletteId = VideoPalettes.CustomId;
            style.CustomPalette = OwnPalette() ?? new VideoPalette(VideoPalettes.CustomId, EditBackground, EditSurface, EditAccent, EditAccent2, EditText,
                EditMuted, Light: false);
            style.CustomPaletteName = Palette.SavedId is not null ? Palette.Label : string.Empty;
        }
        else
        {
            style.PaletteId = Palette.Palette.Id;
            style.CustomPalette = null;
            style.CustomPaletteName = string.Empty;
        }

        style.TitleStyle = (TitleStyle)TitleStyle.Value;
        style.TitlePosition = Position;
        style.DrawStyle = (DrawStyle)DrawStyle.Value;
        style.Motion = (MotionIntensity)Motion.Value;
        style.Tempo = (NarrationTempo)Tempo.Value;
        style.Drawer = (SceneDrawer)Drawer.Value;
        style.WordHighlight = WordHighlight;
        style.Closing = (ClosingPlacement)Closing.Value;
        style.ClosingPlatform = (ClosingPlatform)Platform.Value;
        style.ClosingPhrase = (ClosingPhrase)(Phrase?.Value ?? 0);
        style.UpdatedAtUtc = DateTimeOffset.UtcNow;
        return style;
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPropertyChanged(e);
        if (_filling || e.PropertyName is nameof(Phrases) || e.PropertyName?.StartsWith("Preview", StringComparison.Ordinal) == true
            || e.PropertyName is nameof(ClosingPreview) or nameof(ClosingOn))
        {
            return;
        }

        if (e.PropertyName is nameof(BaseColour) or nameof(GenerateLight) or nameof(PaletteName) or nameof(PaletteStatus) or nameof(PaletteOk)
            or nameof(OwnColours) or nameof(SavedChosen) or nameof(DrawStyleNote) or nameof(SuggestedPalette) or nameof(HasSuggestedPalette)
            or nameof(SuggestedPaletteText))
        {
            return;
        }

        if (e.PropertyName is nameof(DrawStyle) or nameof(Palette))
        {
            ShowDrawStyle();
        }

        if (e.PropertyName == nameof(Palette))
        {
            // A palette chosen: its colours fill the form (shown for the own colours and a saved palette).
            _filling = true;
            Fill(Palette.Palette);
            if (Palette.SavedId is not null)
            {
                PaletteName = Palette.Label;
            }

            _filling = false;
        }
        else if (e.PropertyName is nameof(EditBackground) or nameof(EditSurface) or nameof(EditAccent) or nameof(EditAccent2) or nameof(EditText)
            or nameof(EditMuted))
        {
            // A colour typed: the form now shows the template's own colours (a saved palette stays as it was saved).
            Edited();
        }

        if (e.PropertyName == nameof(Platform))
        {
            // The sentences follow the platform's wording (subscribe / follow); the chosen sentence stays.
            var chosen = Phrase?.Value ?? 0;
            _filling = true;
            Phrases = PhraseChoices((ClosingPlatform)Platform.Value);
            Phrase = Pick(Phrases, chosen);
            _filling = false;
        }

        Refresh();
    }

    /// <summary>The form's colours as a palette; <c>null</c> while one of them is not a colour.</summary>
    public VideoPalette? OwnPalette()
    {
        var values = new[] { EditBackground, EditSurface, EditAccent, EditAccent2, EditText, EditMuted };
        return values.All(v => PaletteDesigner.Normalize(v) is not null)
            ? PaletteDesigner.Own(EditBackground, EditSurface, EditAccent, EditAccent2, EditText, EditMuted)
            : null;
    }

    /// <summary>Fills the own colours with a harmonious palette made from <see cref="BaseColour"/> (nothing while it is not a colour).</summary>
    [RelayCommand]
    public void Generate()
    {
        if (PaletteDesigner.Normalize(BaseColour) is null)
        {
            PaletteStatus = _text["styles.own.base_invalid"];
            PaletteOk = false;
            return;
        }

        var palette = PaletteDesigner.FromBase(BaseColour, GenerateLight);
        _filling = true;
        Fill(palette);
        _filling = false;
        Edited();
        Refresh();
    }

    /// <summary>The saved palettes changed (saved or deleted in the section): the list is rebuilt and <paramref name="select"/> chosen.</summary>
    public void SetSaved(IReadOnlyList<SavedPalette> saved, Guid? select)
    {
        ArgumentNullException.ThrowIfNull(saved);
        var colours = OwnPalette();
        _filling = true;
        for (var i = Palettes.Count - 2; i >= 0; i--)
        {
            if (Palettes[i].SavedId is not null)
            {
                Palettes.RemoveAt(i);
            }
        }

        var at = VideoPalettes.All.Count;
        foreach (var palette in saved)
        {
            Palettes.Insert(at++, Option(palette.Colors, palette.Name, palette.Id));
        }

        Palette = select is { } id ? Palettes.First(p => p.SavedId == id) : colours is null ? Palettes[^1] : ReplaceOwn(colours);
        _filling = false;
        Refresh();
    }

    /// <summary>A colour of the form changed: the own entry takes the colours and is chosen.</summary>
    private void Edited()
    {
        if (_filling)
        {
            return;
        }

        _filling = true;
        if (OwnPalette() is { } colours)
        {
            Palette = ReplaceOwn(colours);
        }
        else if (!Palette.Own)
        {
            Palette = Palettes[^1];
        }

        _filling = false;
    }

    private PaletteOption ReplaceOwn(VideoPalette colours)
    {
        var option = Option(colours, _text["styles.own"], own: true);
        Palettes[^1] = option;
        return option;
    }

    private void Fill(VideoPalette palette)
    {
        EditBackground = palette.Background;
        EditSurface = palette.Surface;
        EditAccent = palette.Accent;
        EditAccent2 = palette.Accent2;
        EditText = palette.Text;
        EditMuted = palette.Muted;
    }

    private string Describe(PaletteCheckResult check)
    {
        var parts = new List<string> { _text.Format("styles.own.contrast", check.TextContrast.ToString("0.0", _text.Culture)) };
        parts.AddRange(check.Errors.Concat(check.Warnings).Select(i => _text["palette.issue." + i.ToString().ToLowerInvariant()]));
        return string.Join(" · ", parts);
    }

    private static PaletteOption Option(VideoPalette p, string label, Guid? saved = null, bool own = false) =>
        new(p, label, [Brush(p.Background), Brush(p.Surface), Brush(p.Accent), Brush(p.Accent2), Brush(p.Text), Brush(p.Muted)], saved, own);

    private void OnCellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_filling || sender is not TitleCellViewModel { IsChecked: true } cell)
        {
            return;
        }

        Position = cell.Position;
    }

    private void Refresh()
    {
        var wasFilling = _filling;
        _filling = true;
        foreach (var cell in TitleCells)
        {
            cell.IsChecked = cell.Position == Position;
        }

        _filling = wasFilling;
        OwnColours = Palette.Editable;
        SavedChosen = Palette.SavedId is not null;
        var own = Palette.Editable ? OwnPalette() : null;
        var check = own is null ? null : PaletteDesigner.Check(own);
        PaletteOk = !Palette.Editable || check is { Ok: true };
        PaletteStatus = !Palette.Editable ? string.Empty : check is null ? _text["palette.issue.invalidcolour"] : Describe(check);
        var palette = check is { Ok: true } ? own! : Palette.Palette;
        var style = (TitleStyle)TitleStyle.Value;
        var chosen = ToStyle();
        PreviewTitleWeight = VideoStyleRules.TitleWeight(chosen) == 700 ? FontWeight.Bold : FontWeight.Normal;
        PreviewTextWeight = VideoStyleRules.SubtitlesBold(chosen, Font.Font) ? FontWeight.Bold : FontWeight.Normal;
        PreviewFontStyle = VideoStyleRules.Italic(chosen) ? FontStyle.Italic : FontStyle.Normal;
        PreviewBackground = Brush(palette.Background);
        PreviewAccent = Brush(palette.Accent);
        PreviewText = Brush(palette.Text);
        PreviewOutline = Color.Parse(palette.Background);
        PreviewReadableOutline = ReadableText ? Color.Parse(PaletteColors.Opposite(palette.Text)) : Colors.Transparent;
        PreviewTitleBox = style switch
        {
            Core.Styles.TitleStyle.Band => Brush(palette.Surface),
            Core.Styles.TitleStyle.Boxed => Brush(palette.Accent),
            _ => Brushes.Transparent,
        };
        PreviewTitleText = style == Core.Styles.TitleStyle.Boxed ? Brush(palette.Background) : Brush(palette.Text);
        PreviewOutlined = style == Core.Styles.TitleStyle.Outline;
        PreviewUnderline = style == Core.Styles.TitleStyle.Underline;
        var anchor = TitleAnchors.Of(Position);
        PreviewTitleHorizontal = anchor.Column switch { 0 => HorizontalAlignment.Left, 2 => HorizontalAlignment.Right, _ => HorizontalAlignment.Center };
        PreviewTitleVertical = anchor.Row switch { 0 => VerticalAlignment.Top, 2 => VerticalAlignment.Bottom, _ => VerticalAlignment.Center };
        PreviewTitleBoxAlignment = style == Core.Styles.TitleStyle.Band ? HorizontalAlignment.Stretch : PreviewTitleHorizontal;
        ClosingOn = (ClosingPlacement)Closing.Value != ClosingPlacement.Off;
        var closing = VideoStyleRules.Closing(ToStyle(), InterfaceContentLanguage());
        ClosingPreview = closing is null ? string.Empty : _text.Format("styles.closing.preview", closing.Sentence, closing.Label);
    }

    private string InterfaceContentLanguage() => _text.Language == "tr" ? ContentLanguages.Turkish : ContentLanguages.English;

    private List<NumberChoice> PhraseChoices(ClosingPlatform platform)
    {
        var table = VideoStyleRules.ClosingFor(InterfaceContentLanguage()) ?? ClosingPhrases.All[0];
        var follow = VideoStyleRules.Wording(platform) == ClosingWording.Follow;
        return table.Lines.Select(l => new NumberChoice((int)l.Phrase, follow ? l.Follow : l.Subscribe)).ToList();
    }

    private List<NumberChoice> Choices<T>(string prefix)
        where T : struct, Enum =>
        Enum.GetValues<T>().Select(v => new NumberChoice(Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture), Label(prefix + Key(v), v.ToString())))
            .ToList();

    private string Label(string key, string fallback) => _text.Has(key) ? _text[key] : fallback;

    private static string Key<T>(T value)
        where T : struct, Enum => value.ToString().ToLowerInvariant();

    private static NumberChoice Pick(IReadOnlyList<NumberChoice> choices, int value) => choices.FirstOrDefault(c => c.Value == value) ?? choices[0];

    /// <summary>The description of the chosen drawing style and the palette it suggests (none when that palette is already chosen).</summary>
    private void ShowDrawStyle()
    {
        if (DrawStyle is null)
        {
            return;
        }

        var definition = Core.Styles.DrawStyles.Of((DrawStyle)DrawStyle.Value);
        var key = "style.draw." + Key(definition.Style) + ".desc";
        var note = _text.Has(key) ? _text[key] : string.Empty;
        DrawStyleNote = definition.PrefersImages && note.Length > 0 ? note + " " + _text["styles.draw.prefers_images"] : note;
        SuggestedPalette = definition.PaletteId.Length == 0 || (Palette is { Editable: false } current && current.Palette.Id == definition.PaletteId)
            ? null
            : Palettes.FirstOrDefault(p => !p.Editable && p.SavedId is null && p.Palette.Id == definition.PaletteId);
    }

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));
}
