using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TMConcept.Core.Videos;
using TMConcept.Desktop.Localization;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// The list of what is missing before production on the video page (F16, 0.1.49; <see cref="ProductionChecklist"/>): every gap by name, a
/// required one (no template, no code model, no key) turns "Produce" off until it is fixed; an optional one (narration off, the template's own
/// music file missing or unreadable, an empty pronunciation dictionary) can be fixed or skipped on purpose ("Skip": a skipped music gap makes
/// the video without its music). Skips last while the app runs and are forgotten with the profile.
/// </summary>
public sealed partial class ProductionChecklistViewModel(Localizer text, Dialogs.IApiKeyDialog keys) : ObservableObject
{
    private readonly HashSet<string> _skipped = new(StringComparer.Ordinal);
    private Func<GapRowViewModel, Task>? _fix;

    /// <summary>The gaps still shown (skipped optional ones are not).</summary>
    public ObservableCollection<GapRowViewModel> Rows { get; } = [];

    /// <summary>Every gap of the last check (skipped ones included).</summary>
    public IReadOnlyList<ProductionGap> Gaps { get; private set; } = [];

    /// <summary>The keys of the skipped gaps (<see cref="ProductionChecklist.Key"/>).</summary>
    public IReadOnlySet<string> Skipped => _skipped;

    /// <summary>A required gap is open: "Produce" is off.</summary>
    [ObservableProperty]
    public partial bool Blocks { get; set; }

    /// <summary>The list shows (something is missing).</summary>
    [ObservableProperty]
    public partial bool HasRows { get; set; }

    /// <summary>Changed: <see cref="Blocks"/> or the rows.</summary>
    public event EventHandler? Changed;

    /// <summary>The page's part of a fix (the model cards opened) and its new check.</summary>
    public void Attach(Func<GapRowViewModel, Task> fix) => _fix = fix ?? throw new ArgumentNullException(nameof(fix));

    /// <summary>Shows the gaps of a new check.</summary>
    public void Apply(IReadOnlyList<ProductionGap> gaps)
    {
        ArgumentNullException.ThrowIfNull(gaps);
        Gaps = gaps;
        Rows.Clear();
        foreach (var gap in ProductionChecklist.Open(gaps, _skipped))
        {
            var detail = gap.Code == ProductionGaps.Key ? keys.ProviderName(gap.Detail) : gap.Detail;
            Rows.Add(new GapRowViewModel(gap, text.Format("video.gap." + gap.Code, detail), text.Has("video.gap." + gap.Code + ".fix")
                ? text["video.gap." + gap.Code + ".fix"]
                : string.Empty));
        }

        Blocks = ProductionChecklist.Blocks(gaps);
        HasRows = Rows.Count > 0;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Skips an optional gap on purpose.</summary>
    [RelayCommand]
    public void Skip(GapRowViewModel? row)
    {
        if (row is not { CanSkip: true })
        {
            return;
        }

        _skipped.Add(ProductionChecklist.Key(row.Gap));
        Apply(Gaps);
    }

    /// <summary>Fixes a gap (its button): a missing key is asked for in the key dialog, the rest where the page can; then the page checks again.</summary>
    [RelayCommand]
    public async Task FixAsync(GapRowViewModel? row)
    {
        if (row is not { CanFix: true })
        {
            return;
        }

        if (row.Gap.Code == ProductionGaps.Key)
        {
            await keys.AddKeyAsync(row.Gap.Detail).ConfigureAwait(true);
        }

        if (_fix is { } fix)
        {
            await fix(row).ConfigureAwait(true);
        }
    }

    /// <summary>Forgets the skips (another profile).</summary>
    public void Reset() => _skipped.Clear();
}
