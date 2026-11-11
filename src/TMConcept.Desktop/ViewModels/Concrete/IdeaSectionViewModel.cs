using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Concepts;
using TMConcept.Desktop.Concepts;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// Section B of the concept page, "Generate from an idea" (0.1.13, the developer: "while reading, another idea came to my mind: I write it
/// and say make 5 concepts of it, or 1, and later one more"): any idea or extra request (it may be empty: then the channel alone drives
/// the concepts) and how many (1–100). It never sends topics (<see cref="IdeaBrief"/>). Every press ADDS to the new concepts; nothing is
/// cleared. Thrown-away duplicates and concepts a cut answer did not reach are not replaced on their own: "Fill the missing n" runs the
/// same idea for exactly the missing count, after a question with the estimate (no money is spent again without a yes).
/// </summary>
public sealed partial class IdeaSectionViewModel(IConfirmDialog confirm, Localizer text, ILogger<IdeaSectionViewModel> logger) : ConceptSectionBase(text, logger)
{
    /// <summary>The idea that ran last (Fill the missing runs this one, not what the box holds now).</summary>
    private IdeaBrief? _last;

    /// <summary>The idea or extra request.</summary>
    [ObservableProperty]
    public partial string Idea { get; set; } = string.Empty;

    /// <summary>How many concepts one press writes (1–100).</summary>
    [ObservableProperty]
    public partial decimal? Count { get; set; } = 5;

    /// <summary>How many the last run did not write.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMissing), nameof(FillMissingText))]
    public partial int Missing { get; set; }

    /// <summary>Whether "Fill the missing" is shown.</summary>
    public bool HasMissing => Missing > 0 && !Running;

    /// <summary>"Fill the missing 3".</summary>
    public string FillMissingText => Text.Format("concepts.fill_missing", Missing);

    /// <inheritdoc />
    public override string GenerateText => Text["concepts.idea.generate"];

    /// <summary>The longest idea.</summary>
    public int IdeaMax => ConceptLimits.ExtraPromptMax;

    /// <summary>
    /// Runs the last idea for the missing count after a question with its estimate. Without a concept model the run itself says what is
    /// missing, before anything is paid for.
    /// </summary>
    [RelayCommand]
    public async Task FillMissingAsync()
    {
        if (_last is not { } last || Missing < 1 || !CanGenerate)
        {
            return;
        }

        var brief = last with { Requested = Missing };
        try
        {
            // The same price as the tiny estimate next to the button (the meaning check included).
            if (await Runner.PriceAsync(brief).ConfigureAwait(true) is { } price)
            {
                var answer = await confirm.AskAsync(Text["concepts.fill_missing.title"],
                    Text.Format("concepts.fill_missing.message", brief.Count, price.ModelName, Text.Usd(price.Estimate.MinUsd), Text.Usd(price.Estimate.MaxUsd)),
                    Text.Format("concepts.fill_missing", brief.Count), danger: false).ConfigureAwait(true);
                if (!answer.Confirmed)
                {
                    return;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Without an estimate nothing is paid for: the reason is shown and nothing starts.
            logger.LogError(ex, "The estimate for the missing concepts failed");
            Status = Text.Format("error.generic", ex.Message);
            StatusIsError = true;
            return;
        }

        await RunAsync(brief).ConfigureAwait(true);
    }

    /// <summary>Another profile: the last run's lines and its "Fill the missing" belong to the old one.</summary>
    public override void Clear()
    {
        base.Clear();
        Missing = 0;
        _last = null;
    }

    /// <inheritdoc />
    protected override ConceptBrief? Brief() =>
        Count is { } count and >= 1 ? new IdeaBrief(Idea, (int)Math.Clamp(count, ConceptLimits.CountMin, ConceptLimits.CountMax)) : null;

    /// <inheritdoc />
    protected override string OwnBlockReason() => Count is null or < 1 ? Text["concepts.idea.count_empty"] : string.Empty;

    /// <inheritdoc />
    protected override void OnFinished(ConceptBrief brief, ConceptRunReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        _last = (IdeaBrief)brief;
        Missing = report.Result is { ErrorCode: not "canceled" } result ? result.Missing : 0;
    }

    partial void OnIdeaChanged(string value) => Refresh();

    partial void OnCountChanged(decimal? value) => Refresh();

    /// <inheritdoc />
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(Running))
        {
            // "Fill the missing" hides while a run goes on.
            OnPropertyChanged(nameof(HasMissing));
        }
    }
}
