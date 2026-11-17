using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Documents;
using TMConcept.Core.Profiles;
using TMConcept.Desktop.Components;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.DiskSpace;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// "Belgelerim" of the open profile (0.1.21): the documents added in this profile, adding files (PDF, Word, text, Markdown), a web address or
/// a Wikipedia article (by address, or by title in a chosen language), deleting one (its copy to the recycle bin, through the storage
/// manager, so nothing is removed while work runs), and installing the meaning model (only after the confirmation with the download list).
/// Every failure is shown on the panel in the user's language and logged. One add at a time.
/// </summary>
public sealed partial class DocumentLibraryViewModel(AppSession session, DocumentLibrary library, IDiskCleaner cleaner, IConfirmDialog confirm,
    ComponentInstallFlow installs, FileDialogs dialogs, IActivityLog activity, Localizer text, ILogger<DocumentLibraryViewModel> logger) : ObservableObject
{
    private CancellationTokenSource? _cancel;

    /// <summary>The profile's documents, the newest first.</summary>
    public ObservableCollection<DocumentRowViewModel> Documents { get; } = [];

    /// <summary>The panel is open.</summary>
    [ObservableProperty]
    public partial bool Open { get; set; }

    /// <summary>A web address or a Wikipedia address or title.</summary>
    [ObservableProperty]
    public partial string Address { get; set; } = string.Empty;

    /// <summary>The language of a Wikipedia title ("tr", "en", …).</summary>
    [ObservableProperty]
    public partial string WikipediaLanguage { get; set; } = "tr";

    /// <summary>Something is being added (or the meaning model works).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Idle))]
    public partial bool Busy { get; set; }

    /// <summary>Nothing is being added.</summary>
    public bool Idle => !Busy;

    /// <summary>What is being done now.</summary>
    [ObservableProperty]
    public partial string Progress { get; set; } = string.Empty;

    /// <summary>What the last action did, or why it failed.</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    /// <summary>The status line is red.</summary>
    [ObservableProperty]
    public partial bool StatusIsError { get; set; }

    /// <summary>Whether the meaning model is installed.</summary>
    public bool EmbedderReady => library.Embedder.Ready;

    /// <summary>The line about the search ("meaning search on" or "keyword search; install the meaning model").</summary>
    public string SearchLine => text[EmbedderReady ? "documents.search.meaning" : "documents.search.keyword"];

    /// <summary>Whether the profile has a document that can be searched.</summary>
    public bool HasReady => Documents.Any(d => d.Ready);

    /// <summary>"3 documents".</summary>
    public string Header => text.Format("documents.header", Documents.Count);

    /// <summary>Whether the list is empty.</summary>
    public bool IsEmpty => Documents.Count == 0;

    /// <summary>The documents changed (added, deleted, another profile, the meaning model installed).</summary>
    public event EventHandler? Changed;

    /// <summary>The ticked ready documents (empty = none ticked: every ready one is used).</summary>
    public IReadOnlyList<Guid> Chosen => Documents.Where(d => d.Checked && d.Ready).Select(d => d.Id).ToList();

    /// <summary>Opens or closes the panel.</summary>
    [RelayCommand]
    public void Toggle() => Open = !Open;

    /// <summary>Closes the panel.</summary>
    [RelayCommand]
    public void Close() => Open = false;

    /// <summary>Reads the open profile's documents again.</summary>
    public async Task LoadAsync()
    {
        var profile = session.Profile;
        var ticked = Documents.Where(d => d.Checked).Select(d => d.Id).ToHashSet();
        IReadOnlyList<SourceDocument> documents = profile is null ? [] : await library.ListAsync(profile.Id, CancellationToken.None).ConfigureAwait(true);
        if (session.Profile?.Id != profile?.Id)
        {
            return;
        }

        foreach (var row in Documents)
        {
            row.PropertyChanged -= OnRow;
        }

        Documents.Clear();
        foreach (var document in documents)
        {
            var row = new DocumentRowViewModel(document, Info(document), Problem(document)) { Checked = ticked.Contains(document.Id) };
            row.PropertyChanged += OnRow;
            Documents.Add(row);
        }

        Raise();
    }

    /// <summary>Another profile: its documents are read, the last lines are forgotten.</summary>
    public async Task ProfileChangedAsync()
    {
        Cancel();
        Status = string.Empty;
        StatusIsError = false;
        Address = string.Empty;
        WikipediaLanguage = session.Profile?.ContentLanguage is { Length: 2 } language ? language : text.Language;
        foreach (var row in Documents)
        {
            row.PropertyChanged -= OnRow;
        }

        Documents.Clear();
        await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>Stops a running add (what was saved stays).</summary>
    [RelayCommand]
    public void Cancel() => _cancel?.Cancel();

    /// <summary>Picks files and adds them.</summary>
    [RelayCommand]
    public async Task PickFilesAsync()
    {
        var patterns = library.Extensions.Select(e => "*" + e).ToList();
        var files = await dialogs.OpenManyAsync("documents.pick", "documents.filter", patterns, multiple: true).ConfigureAwait(true);
        await AddFilesAsync(files).ConfigureAwait(true);
    }

    /// <summary>Adds <paramref name="files"/> one after the other; each one's failure is told and the others go on.</summary>
    public Task AddFilesAsync(IReadOnlyList<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        return RunAsync(files.Select(f => (Path.GetFileName(f), (Func<Guid, IProgress<DocumentProgress>, CancellationToken, Task<DocumentAddResult>>)(
            (profile, progress, token) => library.AddFileAsync(profile, f, progress, token)))).ToList());
    }

    /// <summary>Adds the web page at <see cref="Address"/>.</summary>
    [RelayCommand]
    public Task AddWebAsync() => AddAddressAsync(DocumentKind.Web);

    /// <summary>Adds the Wikipedia article at <see cref="Address"/> (an address, or a title in <see cref="WikipediaLanguage"/>).</summary>
    [RelayCommand]
    public Task AddWikipediaAsync() => AddAddressAsync(DocumentKind.Wikipedia);

    /// <summary>Deletes a document after asking (its copy to the recycle bin; its passages from the database).</summary>
    [RelayCommand]
    public async Task DeleteAsync(DocumentRowViewModel? row)
    {
        if (row is null || Busy)
        {
            return;
        }

        var answer = await confirm.AskAsync(text["documents.delete.title"], text.Format("documents.delete.message", row.Title), text["documents.delete"], danger: true)
            .ConfigureAwait(true);
        if (!answer.Confirmed)
        {
            return;
        }

        try
        {
            var item = new DocumentDiskItem(row.Document, session.Profile?.Name ?? string.Empty, library, 0,
                row.Document.StoredFile.Length > 0 ? library.FileOf(row.Document) : string.Empty);
            var result = await cleaner.RemoveAsync([item], CancellationToken.None).ConfigureAwait(true);
            if (result.Busy)
            {
                Say(text["storage.busy"], error: true);
            }
            else if (result.Failed.Count > 0)
            {
                Say(text.Format("storage.item_failed", row.Title, result.Failed[0].Reason), error: true);
            }
            else if (result.Kept.Count > 0)
            {
                Say(text.Format("storage.kept", row.Title), error: true);
            }
            else
            {
                Say(text.Format("documents.deleted", row.Title), error: false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "The document could not be deleted");
            Say(text.Format("documents.delete_failed", row.Title, ex.Message), error: true);
        }

        await LoadAsync().ConfigureAwait(true);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Takes away the documents a research run added when the user chose not to keep them (0.1.22): no question (the choice was made on
    /// the section), through the recycle bin like a delete. Returns the reason when some could not be taken away (empty = all went).
    /// </summary>
    public async Task<string> RemoveAddedAsync(IReadOnlyCollection<Guid> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0 || session.Profile is not { } profile)
        {
            return string.Empty;
        }

        var documents = (await library.ListAsync(profile.Id, CancellationToken.None).ConfigureAwait(true)).Where(d => ids.Contains(d.Id)).ToList();
        var items = documents.Select(d => (Core.DiskSpace.IDiskItem)new DocumentDiskItem(d, profile.Name, library, d.Bytes,
            d.StoredFile.Length > 0 ? library.FileOf(d) : string.Empty)).ToList();
        var reason = string.Empty;
        if (items.Count > 0)
        {
            var result = await cleaner.RemoveAsync(items, CancellationToken.None).ConfigureAwait(true);
            reason = result.Busy ? text["storage.busy"]
                : result.Failed.Count > 0 ? text.Format("storage.item_failed", result.Failed[0].Item.Name, result.Failed[0].Reason)
                : string.Empty;
            logger.LogInformation("Research: {Count} fetched pages the user did not keep were taken away", result.Removed.Count);
        }

        await LoadAsync().ConfigureAwait(true);
        Changed?.Invoke(this, EventArgs.Empty);
        return reason;
    }

    /// <summary>Reads the documents again and tells the sections (research added some).</summary>
    public async Task RefreshAsync()
    {
        await LoadAsync().ConfigureAwait(true);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Installs the meaning model (after the confirmation that lists the download) and then makes the vectors of every passage that has
    /// none yet. Declining changes nothing: the documents stay searchable by their words.
    /// </summary>
    [RelayCommand]
    public async Task InstallEmbedderAsync()
    {
        if (Busy)
        {
            return;
        }

        var outcome = await installs.EnsureAsync([library.Embedder.ComponentCode], "install.purpose.documents", null, CancellationToken.None).ConfigureAwait(true);
        OnPropertyChanged(nameof(EmbedderReady));
        OnPropertyChanged(nameof(SearchLine));
        if (!outcome.Ready)
        {
            if (outcome.Reason.Length > 0)
            {
                Say(outcome.Reason, error: true);
            }

            return;
        }

        await IndexAsync().ConfigureAwait(true);
    }

    /// <summary>Makes the meaning-search vectors of the passages that have none (the model must be installed).</summary>
    public async Task IndexAsync()
    {
        using var cancel = new CancellationTokenSource();
        _cancel = cancel;
        Busy = true;
        try
        {
            var made = await library.EmbedMissingAsync(new Progress<DocumentProgress>(Show), cancel.Token).ConfigureAwait(true);
            Say(text.Format("documents.indexed", made), error: false);
        }
        catch (OperationCanceledException)
        {
            Say(text["documents.index_canceled"], error: false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "The documents' meaning-search vectors could not be made");
            Say(text.Format("documents.index_failed", ex.Message), error: true);
        }
        finally
        {
            _cancel = null;
            Busy = false;
            Progress = string.Empty;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The user's text for a rejected document.</summary>
    public string Reason(DocumentRejectedException error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var key = "documents.fail." + error.Code;
        return text.Has(key) ? text.Format(key, error.Detail) : text.Format("documents.fail.unreadable", error.Detail.Length > 0 ? error.Detail : error.Message);
    }

    private Task AddAddressAsync(DocumentKind kind)
    {
        var address = Address.Trim();
        if (address.Length == 0)
        {
            Say(text["documents.address.empty"], error: true);
            return Task.CompletedTask;
        }

        var language = WikipediaLanguage.Trim();
        return RunAsync([(address, (profile, progress, token) => library.AddWebAsync(profile, kind, address, language, progress, token))], clearAddress: true);
    }

    private async Task RunAsync(IReadOnlyList<(string Name, Func<Guid, IProgress<DocumentProgress>, CancellationToken, Task<DocumentAddResult>> Add)> work,
        bool clearAddress = false)
    {
        if (work.Count == 0)
        {
            return;
        }

        if (session.Profile is not { } profile)
        {
            Say(text["profile.needed"], error: true);
            session.AskForProfile();
            return;
        }

        if (Busy)
        {
            return;
        }

        using var cancel = new CancellationTokenSource();
        _cancel = cancel;
        Busy = true;
        var added = 0;
        var failures = new List<string>();
        var notes = new List<string>();
        try
        {
            foreach (var (name, add) in work)
            {
                try
                {
                    var result = await add(profile.Id, new Progress<DocumentProgress>(Show), cancel.Token).ConfigureAwait(true);
                    added++;
                    if (result.Document.Status == DocumentStatus.NoText)
                    {
                        notes.Add(text.Format("documents.added_no_text", result.Document.Title));
                    }

                    if (result.EmbeddingError.Length > 0)
                    {
                        notes.Add(text.Format("documents.index_failed", result.EmbeddingError));
                    }
                }
                catch (DocumentRejectedException ex)
                {
                    logger.LogWarning("A document was not added ({Code})", ex.Code);
                    failures.Add(name + ": " + Reason(ex));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    logger.LogError(ex, "A document could not be added");
                    failures.Add(name + ": " + ex.Message);
                }
            }
        }
        catch (OperationCanceledException)
        {
            notes.Add(text["documents.add_canceled"]);
        }
        finally
        {
            _cancel = null;
            Busy = false;
            Progress = string.Empty;
        }

        if (clearAddress && added > 0)
        {
            Address = string.Empty;
        }

        var line = string.Join("\n", new[] { added > 0 ? text.Format("documents.added", added) : string.Empty }.Concat(notes).Concat(failures).Where(l => l.Length > 0));
        Say(line, error: failures.Count > 0);
        await LoadAsync().ConfigureAwait(true);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Show(DocumentProgress progress)
    {
        Progress = progress.Stage == DocumentStage.Embedding && progress.Total > 0
            ? text.Format("documents.stage.embedding_count", progress.Name, progress.Done, progress.Total)
            : text.Format("documents.stage." + progress.Stage.ToString().ToLowerInvariant(), progress.Name);
    }

    private void Say(string line, bool error)
    {
        Status = line;
        StatusIsError = error;
        if (line.Length > 0)
        {
            activity.Add(error ? ActivityLevel.Error : ActivityLevel.Info, line);
        }
    }

    private string Info(SourceDocument document)
    {
        var parts = new List<string> { text["documents.kind." + document.Kind.ToString().ToLowerInvariant()] };
        if (document.Pages > 0)
        {
            parts.Add(text.Format("documents.pages", document.Pages));
        }

        parts.Add(text.Format("documents.passages", document.Chunks));
        parts.Add(Megabytes(document.Bytes));
        if (document.Kind == DocumentKind.Wikipedia && document.Language.Length > 0)
        {
            parts.Add(document.Language);
        }

        return string.Join(" · ", parts);
    }

    private string Problem(SourceDocument document) => document.Status == DocumentStatus.NoText
        ? text[document.Kind == DocumentKind.Pdf ? "documents.problem.scan" : "documents.problem.empty"]
        : document.StatusDetail == "cut" ? text["documents.problem.cut"] : string.Empty;

    private static string Megabytes(long bytes) => bytes < 1_000_000
        ? Math.Max(1, (int)Math.Round(bytes / 1000.0)).ToString(CultureInfo.InvariantCulture) + " KB"
        : (bytes / 1_000_000.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB";

    private void OnRow(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentRowViewModel.Checked))
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Raise()
    {
        OnPropertyChanged(nameof(HasReady));
        OnPropertyChanged(nameof(Header));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmbedderReady));
        OnPropertyChanged(nameof(SearchLine));
    }
}
