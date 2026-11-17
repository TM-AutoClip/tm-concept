using System.ComponentModel;
using Avalonia.Layout;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using TMConcept.Core.Profiles;
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

    /// <summary>Fills the form from <paramref name="source"/> (a copy is edited; the source is not changed).</summary>
    public VideoStyleEditorViewModel(VideoStyle source, bool isNew, Localizer text)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(text);
        _source = VideoStyleRules.Copy(source);
        _text = text;
        IsNew = isNew;
        Palettes = VideoPalettes.All.Select(p => new PaletteOption(p, Label("style.palette." + p.Id, p.Id),
            [Brush(p.Background), Brush(p.Surface), Brush(p.Accent), Brush(p.Accent2), Brush(p.Text), Brush(p.Muted)])).ToList();
        Fonts = VideoFonts.All.Select(f => new FontOption(f, VideoFontPreview.For(f))).ToList();
        TitleStyles = Choices<TitleStyle>("style.title.");
        DrawStyles = Choices<DrawStyle>("style.draw.");
        Motions = Choices<MotionIntensity>("style.motion.");
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
        ProgressBar = _source.ProgressBar;
        Font = Fonts.FirstOrDefault(f => f.Font.Id == _source.FontId) ?? Fonts[0];
        Palette = Palettes.FirstOrDefault(p => p.Palette.Id == _source.PaletteId) ?? Palettes[0];
        TitleStyle = Pick(TitleStyles, (int)_source.TitleStyle);
        DrawStyle = Pick(DrawStyles, (int)_source.DrawStyle);
        Motion = Pick(Motions, (int)_source.Motion);
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

    /// <summary>The colour palettes with their swatches.</summary>
    public IReadOnlyList<PaletteOption> Palettes { get; }

    /// <summary>The video fonts, each named in its own font.</summary>
    public IReadOnlyList<FontOption> Fonts { get; }

    /// <summary>Title styles.</summary>
    public IReadOnlyList<NumberChoice> TitleStyles { get; }

    /// <summary>Drawing styles (scenes without images; also the style of generated images).</summary>
    public IReadOnlyList<NumberChoice> DrawStyles { get; }

    /// <summary>Motion intensities.</summary>
    public IReadOnlyList<NumberChoice> Motions { get; }

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

    /// <summary>Font.</summary>
    [ObservableProperty]
    public partial FontOption Font { get; set; }

    /// <summary>Colour palette.</summary>
    [ObservableProperty]
    public partial PaletteOption Palette { get; set; }

    /// <summary>Title style.</summary>
    [ObservableProperty]
    public partial NumberChoice TitleStyle { get; set; }

    /// <summary>Drawing style.</summary>
    [ObservableProperty]
    public partial NumberChoice DrawStyle { get; set; }

    /// <summary>Motion intensity.</summary>
    [ObservableProperty]
    public partial NumberChoice Motion { get; set; }

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
        style.FontId = Font.Font.Id;
        style.PaletteId = Palette.Palette.Id;
        style.TitleStyle = (TitleStyle)TitleStyle.Value;
        style.TitlePosition = Position;
        style.DrawStyle = (DrawStyle)DrawStyle.Value;
        style.Motion = (MotionIntensity)Motion.Value;
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
        var palette = Palette.Palette;
        var style = (TitleStyle)TitleStyle.Value;
        PreviewBackground = Brush(palette.Background);
        PreviewAccent = Brush(palette.Accent);
        PreviewText = Brush(palette.Text);
        PreviewOutline = Color.Parse(palette.Background);
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

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));
}
