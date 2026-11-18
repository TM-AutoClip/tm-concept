using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Concepts;
using TMConcept.Core.Documents;
using TMConcept.Core.Research;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.Work;
using TMConcept.Documents.Research;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// Section D of the concept page, "Araştırmadan" (0.1.22): the app researches a subject itself and writes concepts from what it found.
/// "Search" asks the backends of Settings → Research (Wikipedia and Wikidata by default, the addresses typed here, Tavily with the user's
/// own key, the user's MCP servers when switched on) in the profile's language, English and the languages the subject points at, and shows
/// the pages it WOULD fetch, ticked by the plan; nothing is fetched yet and no model is called. The button then fetches the ticked pages
/// within the page, byte and time limits (cancellable, its steps in the activity panel), saves them into the profile's documents
/// ("Belgelerim": passages, meaning search, citations) and writes the concepts from them alone, in the profile's language, through the
/// documents prompt (everything fetched is data, never instructions). When the user does not keep the results, the pages this run added are
/// deleted after the run, also when it was canceled or failed midway. The button writes only from what the last search found
/// for the subject, addresses and languages now in the boxes (a change asks for a new search), and at most "Pages" ticked pages. Like the
/// other sections it only ADDS to the concept list and shares the page's one call at a time; the model may be online or local.
/// </summary>
public sealed partial class ResearchSectionViewModel(AppSession session, ResearchRun run, ResearchBackendSet backends, IActivityLog activity, WorkStatus work,
    Localizer text, ILogger<ResearchSectionViewModel> logger) : ConceptSectionBase(text, logger)
{
    private CancellationTokenSource? _searchCancel;
    private DocumentLibraryViewModel? _library;
    private IReadOnlyList<Guid> _added = [];
    private Dictionary<string, string> _backendNames = new(StringComparer.Ordinal);
    private string? _searchedFor;
    private bool _loaded;

    /// <summary>The subject.</summary>
    [ObservableProperty]
    public partial string Subject { get; set; } = string.Empty;

    /// <summary>How many concepts a press writes (1–30).</summary>
    [ObservableProperty]
    public partial decimal? Count { get; set; } = 3;

    /// <summary>Extra languages to read ("fr, de"), besides the profile's, English and what the subject points at.</summary>
    [ObservableProperty]
    public partial string Languages { get; set; } = string.Empty;

    /// <summary>The user's own addresses, one per line (always fetched first).</summary>
    [ObservableProperty]
    public partial string Urls { get; set; } = string.Empty;

    /// <summary>Pages fetched at most.</summary>
    [ObservableProperty]
    public partial decimal? Pages { get; set; } = ResearchLimits.PagesDefault;

    /// <summary>The fetched pages stay in "Belgelerim".</summary>
    [ObservableProperty]
    public partial bool KeepInLibrary { get; set; } = true;

    /// <summary>The pages the last search found.</summary>
    public ObservableCollection<ResearchCandidateViewModel> Candidates { get; } = [];

    /// <summary>Whether there are found pages.</summary>
    public bool HasCandidates => Candidates.Count > 0;

    /// <summary>How many pages are ticked.</summary>
    public int CheckedCount => Candidates.Count(c => c.Checked);

    /// <summary>The longest subject.</summary>
    public int SubjectMax => ResearchLimits.SubjectMax;

    /// <summary>The most pages.</summary>
    public int PagesMax => ResearchLimits.PagesMax;

    /// <summary>The plan of the last search ("Languages: tr, en, el · Wikipedia, Wikidata").</summary>
    [ObservableProperty]
    public partial string PlanLine { get; set; } = string.Empty;

    /// <summary>A search runs.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSearch), nameof(CanGenerate))]
    public partial bool Searching { get; set; }

    /// <summary>The running search's step.</summary>
    [ObservableProperty]
    public partial string SearchProgress { get; set; } = string.Empty;

    /// <summary>What the last search did.</summary>
    [ObservableProperty]
    public partial string SearchStatus { get; set; } = string.Empty;

    /// <summary>The last search failed or found nothing.</summary>
    [ObservableProperty]
    public partial bool SearchFailed { get; set; }

    /// <summary>Whether "Search" can be pressed.</summary>
    public bool CanSearch => !Searching && !Running && !RunnerBusy && (Subject.Trim().Length > 0 || Urls.Trim().Length > 0);

    /// <inheritdoc />
    public override string GenerateText => Text.Format("research.generate", CheckedCount, Asked());

    /// <inheritdoc />
    protected override bool Paused => Searching;

    /// <summary>Uses the page's "Belgelerim" (the pages are saved there) and the research settings.</summary>
    public void AttachLibrary(DocumentLibraryViewModel library)
    {
        ArgumentNullException.ThrowIfNull(library);
        _library = library;
        library.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(DocumentLibraryViewModel.Busy))
            {
                Refresh();
            }
        };
    }

    /// <summary>Reads the research settings into the section (once; later the section's own choices are saved back).</summary>
    public void LoadSettings()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        var research = session.Settings.Research;
        Languages = research.ExtraLanguages;
        Pages = Math.Clamp(research.Pages, 1, ResearchLimits.PagesMax);
        KeepInLibrary = research.KeepInLibrary;
    }

    /// <summary>
    /// "Search": asks the backends of the settings and shows the pages the plan would fetch (ticked). Nothing is fetched, no model is
    /// called; every failing backend is shown with its reason and the others go on.
    /// </summary>
    [RelayCommand]
    public async Task SearchAsync()
    {
        if (session.Profile is not { } profile)
        {
            Refuse(Text["profile.needed"]);
            session.AskForProfile();
            return;
        }

        if (Subject.Trim().Length == 0 && Urls.Trim().Length == 0)
        {
            Refuse(Text["research.subject_empty"]);
            return;
        }

        if (Searching || Running || !Runner.TryEnter(this))
        {
            Refuse(Text["concepts.busy_other"]);
            return;
        }

        SearchFailed = false;
        SearchStatus = string.Empty;
        Searching = true;
        var searchedFor = SearchKey();
        using var cancel = new CancellationTokenSource();
        _searchCancel = cancel;
        IReadOnlyList<IResearchBackend> asked = [];
        try
        {
            await SaveSettingsAsync().ConfigureAwait(true);
            asked = await backends.ForRunAsync(session.Settings.Research, cancel.Token).ConfigureAwait(true);
            var request = Request(profile.ContentLanguage);
            var found = await run.SearchAsync(request, asked, new Progress<ResearchProgress>(ShowSearch), cancel.Token).ConfigureAwait(true);
            if (session.Profile?.Id != profile.Id)
            {
                return;
            }

            Fill(found, asked);
            _searchedFor = searchedFor;
            PlanLine = Text.Format("research.plan", string.Join(", ", found.Plan.Languages), string.Join(", ", asked.Where(b => found.Plan.Queries.Any(q => q.BackendId == b.Id))
                .Select(b => b.Name)));
            var failures = found.Failures.Select(f => f.Source + ": " + Reason(f)).Distinct(StringComparer.Ordinal).ToList();
            SearchFailed = found.Candidates.Count == 0;
            SearchStatus = string.Join("\n", new[]
            {
                found.Candidates.Count == 0 ? Text["research.found_none"] : Text.Format("research.found", found.Candidates.Count, CheckedCount),
                found.Cut ? Text["research.search_cut"] : string.Empty,
            }.Concat(failures).Where(l => l.Length > 0));
            activity.Add(SearchFailed || failures.Count > 0 ? ActivityLevel.Warning : ActivityLevel.Info, SearchStatus.Replace('\n', ' '));
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Research search canceled");
            SearchStatus = Text["research.search_canceled"];
            activity.Add(ActivityLevel.Warning, SearchStatus);
        }
        catch (Exception ex) when (ex is DocumentRejectedException or IOException or InvalidOperationException or HttpRequestException)
        {
            logger.LogError(ex, "Research search failed");
            SearchFailed = true;
            SearchStatus = Text.Format("research.search_failed", ex.Message);
            activity.Add(ActivityLevel.Error, SearchStatus);
        }
        finally
        {
            await ResearchBackendSet.DisposeAsync(asked).ConfigureAwait(true);
            _searchCancel = null;
            SearchProgress = string.Empty;
            work.Clear(this);
            Searching = false;
            Runner.Leave(this);
            Refresh();
        }
    }

    /// <summary>Stops a running search.</summary>
    [RelayCommand]
    public void CancelSearch() => _searchCancel?.Cancel();

    /// <summary>Drops the found pages.</summary>
    [RelayCommand]
    public void ClearCandidates()
    {
        foreach (var candidate in Candidates)
        {
            candidate.PropertyChanged -= OnTicked;
        }

        Candidates.Clear();
        PlanLine = string.Empty;
        _searchedFor = null;
        CandidatesChanged();
    }

    /// <summary>Stops whatever this section runs.</summary>
    public void CancelAll()
    {
        CancelSearch();
        Cancel();
    }

    /// <summary>Another profile: its pages and lines are not these.</summary>
    public override void Clear()
    {
        base.Clear();
        ClearCandidates();
        SearchStatus = string.Empty;
        SearchFailed = false;
    }

    /// <inheritdoc />
    protected override ConceptBrief? Brief() =>
        CheckedCount > 0 && Count is >= 1
            ? new ResearchBrief(Subject, Asked()) { Phase = System.Security.Cryptography.RandomNumberGenerator.GetInt32(1_000) / 1_000.0 }
            : null;

    /// <inheritdoc />
    protected override string OwnBlockReason()
    {
        if (_library?.Busy == true)
        {
            return Text["documents.busy"];
        }

        if (CheckedCount == 0)
        {
            return Text[HasCandidates ? "research.none_ticked" : "research.search_first"];
        }

        if (!string.Equals(_searchedFor, SearchKey(), StringComparison.Ordinal))
        {
            return Text["research.search_again"];
        }

        if (CheckedCount > MaxPages())
        {
            return Text.Format("research.too_many", MaxPages());
        }

        return Count is null or < 1 ? Text["concepts.idea.count_empty"] : string.Empty;
    }

    /// <summary>
    /// Before the model call: the ticked pages are fetched and saved into "Belgelerim" (the page's one call is held meanwhile, so no other
    /// section starts), then the brief carries exactly those documents. Nothing usable = no model call, with the reasons.
    /// </summary>
    protected override async Task<ConceptBrief?> PrepareRunAsync(ConceptBrief brief, CancellationToken cancellationToken)
    {
        if (brief is not ResearchBrief research)
        {
            return brief;
        }

        if (session.Profile is not { } profile)
        {
            ShowStatus(Text["profile.needed"], error: true);
            session.AskForProfile();
            return null;
        }

        if (!Runner.TryEnter(this))
        {
            ShowStatus(Text["concepts.busy_other"], error: false, warning: true);
            return null;
        }

        var picked = Candidates.Where(c => c.Checked).Select(c => c.Hit).ToList();
        IReadOnlyList<IResearchBackend> asked = [];
        var added = new List<Guid>();
        ResearchSaved? saved = null;
        string? stopped = null;
        try
        {
            await SaveSettingsAsync().ConfigureAwait(true);
            asked = await backends.ForRunAsync(session.Settings.Research, cancellationToken).ConfigureAwait(true);
            saved = await run.SaveAsync(profile.Id, picked, asked, Request(profile.ContentLanguage), added, new Progress<ResearchProgress>(ShowFetch), cancellationToken)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Research fetching canceled after {Added} new pages", added.Count);
            stopped = Text[KeepInLibrary || added.Count == 0 ? "research.fetch_canceled" : "research.fetch_canceled_dropped"];
            ShowStatus(stopped, error: false, warning: true);
        }
        catch (Exception ex) when (ex is DocumentRejectedException or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Research fetching failed after {Added} new pages", added.Count);
            stopped = Text.Format("research.fetch_failed", ex.Message);
            ShowStatus(stopped, error: true);
        }
        finally
        {
            await ResearchBackendSet.DisposeAsync(asked).ConfigureAwait(true);
            work.Clear(this);
            Runner.Leave(this);
        }

        // What this run added is known even when it stopped midway: the lists show it, and it is taken away when it is not kept.
        _added = added;
        if (_library is not null)
        {
            await _library.RefreshAsync().ConfigureAwait(true);
        }

        if (saved is null)
        {
            activity.Add(StatusIsError ? ActivityLevel.Error : ActivityLevel.Warning, stopped ?? Status);
            await SafeAsync(() => AfterRunAsync(research)).ConfigureAwait(true);
            return null;
        }

        var skipped = saved.Skipped.Select(f => f.Source + ": " + Reason(f.Code == "backend" && _backendNames.TryGetValue(f.Detail, out var name) ? f with { Detail = name } : f))
            .ToList();
        var line = Text.Format("research.saved", saved.Documents.Count, picked.Count) + (saved.Cut ? " " + Text["research.fetch_cut"] : string.Empty);
        activity.Add(skipped.Count > 0 || saved.Cut ? ActivityLevel.Warning : ActivityLevel.Info, string.Join(" · ", new[] { line }.Concat(skipped)));
        if (saved.Documents.Count == 0)
        {
            ShowStatus(string.Join("\n", new[] { Text["research.saved_none"] }.Concat(skipped.Take(3))), error: true);
            await AfterRunAsync(research).ConfigureAwait(true);
            return null;
        }

        Progress = line;
        return research with { DocumentIds = saved.Documents.Select(d => d.Id).ToList() };
    }

    /// <inheritdoc />
    protected override async Task AfterRunAsync(ConceptBrief ran)
    {
        var added = _added;
        _added = [];
        if (KeepInLibrary || added.Count == 0 || _library is null)
        {
            return;
        }

        var reason = await _library.RemoveAddedAsync(added).ConfigureAwait(true);
        activity.Add(reason.Length > 0 ? ActivityLevel.Warning : ActivityLevel.Info,
            reason.Length > 0 ? Text.Format("research.removed_failed", reason) : Text.Format("research.removed", added.Count));
    }

    /// <inheritdoc />
    protected override void OnRefresh()
    {
        OnPropertyChanged(nameof(CanSearch));
        OnPropertyChanged(nameof(GenerateText));
    }

    partial void OnSubjectChanged(string value) => Refresh();

    partial void OnUrlsChanged(string value) => Refresh();

    partial void OnCountChanged(decimal? value) => Refresh();

    partial void OnLanguagesChanged(string value) => Refresh();

    partial void OnPagesChanged(decimal? value) => Refresh();

    private int MaxPages() => Math.Clamp((int)(Pages ?? ResearchLimits.PagesDefault), 1, ResearchLimits.PagesMax);

    /// <summary>What a search is for: the subject, the addresses and the extra languages as the boxes hold them now.</summary>
    private string SearchKey() => string.Join("\n", Subject.Trim(), string.Join(" ", Urls.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)),
        string.Join(",", ResearchText.Languages(Languages)));

    private int Asked() => Math.Clamp((int)(Count ?? 1), ConceptLimits.CountMin, DocumentLimits.ConceptCountMax);

    private ResearchRequest Request(string language) => new(Subject, language)
    {
        ExtraLanguages = ResearchText.Languages(Languages),
        Urls = Urls.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(ResearchLimits.UrlsMax).ToList(),
        MaxPages = MaxPages(),
    };

    private async Task SaveSettingsAsync()
    {
        var settings = session.Settings;
        var research = settings.Research;
        var pages = MaxPages();
        var languages = string.Join(", ", ResearchText.Languages(Languages));
        if (research.Pages == pages && research.ExtraLanguages == languages && research.KeepInLibrary == KeepInLibrary)
        {
            return;
        }

        research.Pages = pages;
        research.ExtraLanguages = languages;
        research.KeepInLibrary = KeepInLibrary;
        await session.SaveAsync(settings, CancellationToken.None).ConfigureAwait(true);
    }

    private void Fill(ResearchCandidates found, IReadOnlyList<IResearchBackend> asked)
    {
        ClearCandidates();
        _backendNames = asked.GroupBy(b => b.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.Ordinal);
        foreach (var candidate in found.Candidates)
        {
            var name = asked.FirstOrDefault(b => b.Id == candidate.Hit.BackendId)?.Name ?? candidate.Hit.BackendId;
            var item = new ResearchCandidateViewModel(candidate.Hit, candidate.Hit.Language.Length > 0 ? name + " · " + candidate.Hit.Language : name, candidate.Picked);
            item.PropertyChanged += OnTicked;
            Candidates.Add(item);
        }

        CandidatesChanged();
    }

    private string Reason(ResearchFailure failure)
    {
        var key = Text.Has("research.fail." + failure.Code) ? "research.fail." + failure.Code
            : Text.Has("documents.fail." + failure.Code) ? "documents.fail." + failure.Code
            : "documents.fail.unreadable";
        return Text.Format(key, failure.Detail);
    }

    private void Refuse(string reason)
    {
        SearchFailed = true;
        SearchStatus = reason;
        activity.Add(ActivityLevel.Warning, reason);
    }

    private void ShowSearch(ResearchProgress step)
    {
        if (step.Stage == ResearchStage.Searching)
        {
            SearchProgress = Text.Format("research.stage.searching", step.Name, step.Done, step.Total);
            work.Show(this, Text.Format("work.research", SearchProgress));
        }
    }

    private void ShowFetch(ResearchProgress step)
    {
        if (step.Stage is ResearchStage.Fetching or ResearchStage.Saving)
        {
            Progress = Text.Format("research.stage." + step.Stage.ToString().ToLowerInvariant(), step.Name, step.Done + 1, step.Total);
            work.Show(this, Text.Format("work.research", Progress));
        }
    }

    private void OnTicked(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ResearchCandidateViewModel.Checked))
        {
            CandidatesChanged();
        }
    }

    private void CandidatesChanged()
    {
        OnPropertyChanged(nameof(HasCandidates));
        OnPropertyChanged(nameof(CheckedCount));
        OnPropertyChanged(nameof(GenerateText));
        if (!Running)
        {
            Refresh();
        }
    }
}
