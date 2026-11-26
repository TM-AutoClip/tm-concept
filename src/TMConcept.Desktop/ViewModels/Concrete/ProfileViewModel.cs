using System.Collections.ObjectModel;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Paths;
using TMConcept.Core.Profiles;
using TMConcept.Core.Templates;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;
using TMConcept.Storage.Backup;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// Profile (channel) editor: a ready template fills the fields (the template itself never changes), channel name, content language,
/// format, target length, the channel's concept and the channel photo (0.1.19: a round avatar for the closing end card of the templates,
/// with the rights warning). The models are chosen on the pages that use them. A new profile can take a photo before it is saved: it is
/// copied into the library only by Save, and Cancel (or starting another new profile) drops it without a trace. The photo of a saved profile
/// is replaced or removed by Save too; its old copy is deleted then (unless another profile points at it; the copy's name carries the
/// profile's id). A save copies the picked photo before it writes the row, so one that fails writes nothing and removes its copy. Saving
/// with a required value missing is not possible. Deleting a profile is confirmed (with what goes and its size) and permanent, with
/// everything it generated: videos, the photo, documents and work files (<see cref="Core.Deletion.ProfileDeletion"/>; when a file is in
/// use the profile stays and the files are listed). "Kanaldan esinlen" (0.1.53) opens a window that drafts a NEW profile from a channel's
/// public details and measured samples (<see cref="IChannelInspirationDialog"/>); its "Profili oluştur" fills a new profile here and saves it
/// with the same checks and photo copy as Save (<see cref="CreateFromChannelAsync"/>), so the profile being edited is never written over.
/// </summary>
public sealed partial class ProfileViewModel(AppSession session, IProfileStore profiles, ITemplateSource templates, IAppPaths paths, FileDialogs dialogs,
    IConfirmDialog confirm, IActivityLog activity, Localizer text, ILogger<ProfileViewModel> logger, BackupService backup,
    Core.Deletion.ProfileDeletion deletion, IChannelInspirationDialog? inspiration = null)
    : PageViewModelBase
{
    /// <summary>The channel photo's folder under the profile's library folder.</summary>
    private const string PhotoFolder = "channel";

    private Profile? _editing;

    // The channel photo chosen but not saved yet (a file anywhere), and whether Save removes the stored one.
    private string? _photoSource;
    private bool _photoRemoved;

    // The photo the stored row points at (from the last load or successful save): the old copy a save replaces, and what a failed save
    // puts back.
    private string? _storedPhoto;
    private Func<Task>? _profilesChanged;

    /// <inheritdoc />
    public override string TitleKey => "nav.profile";

    /// <inheritdoc />
    public override int Order => 50;

    /// <inheritdoc />
    public override bool ChangesSettings => true;

    /// <summary>Templates.</summary>
    public ObservableCollection<TemplateOption> Templates { get; } = [];

    /// <summary>Chosen template.</summary>
    [ObservableProperty]
    public partial TemplateOption? SelectedTemplate { get; set; }

    /// <summary>
    /// Output languages (concept, narration, subtitles and on-screen text are written in it): the language's own name with its name in the
    /// interface language ("Deutsch — Almanca"). The channel concept and the extra request may still be written in the interface language.
    /// </summary>
    public IReadOnlyList<ChoiceOption> Languages => _languages ??= LanguageOptions.For(text);

    private IReadOnlyList<ChoiceOption>? _languages;

    /// <summary>Whether a new profile is being created.</summary>
    [ObservableProperty]
    public partial bool IsNew { get; set; }

    /// <summary>Channel name.</summary>
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    /// <summary>Content language.</summary>
    [ObservableProperty]
    public partial ChoiceOption? Language { get; set; }

    /// <summary>Vertical short video.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Horizontal))]
    public partial bool Vertical { get; set; } = true;

    /// <summary>Horizontal video.</summary>
    public bool Horizontal
    {
        get => !Vertical;
        set => Vertical = !value;
    }

    /// <summary>Target length (seconds).</summary>
    [ObservableProperty]
    public partial decimal? TargetSeconds { get; set; } = 75;

    /// <summary>The channel's content (what the channel covers; the most important input of every concept and topic prompt).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConceptTextShort))]
    public partial string ConceptText { get; set; } = string.Empty;

    /// <summary>The channel content is written but very short: a one-line warning under the box.</summary>
    public bool ConceptTextShort => ConceptText.Trim().Length is > 0 and < ProfileLimits.ConceptTextAdvised;

    /// <summary>The channel photo shown in the round preview (the chosen file before Save, the stored copy after); <c>null</c> when none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChannelPhoto))]
    public partial string? ChannelPhotoPath { get; set; }

    /// <summary>A channel photo is shown.</summary>
    public bool HasChannelPhoto => ChannelPhotoPath is not null;

    /// <summary>Whether there is a profile to edit or create.</summary>
    [ObservableProperty]
    public partial bool HasProfile { get; set; }

    /// <summary>Lets the shell refresh its profile list after a save or delete.</summary>
    public void OnProfilesChanged(Func<Task> refresh) => _profilesChanged = refresh;

    /// <summary>Starts a new profile.</summary>
    public void StartNew()
    {
        LoadTemplates();
        _editing = new Profile { ContentLanguage = text.Language == "tr" ? ContentLanguages.Turkish : ContentLanguages.English };
        IsNew = true;
        Fill(_editing);
        HasProfile = true;
        Status = string.Empty;
    }

    /// <inheritdoc />
    public override async Task ActivateAsync()
    {
        LoadTemplates();
        if (IsNew && _editing is not null)
        {
            return;
        }

        if (session.Profile is not { } current)
        {
            StartNew();
            return;
        }

        _editing = await profiles.GetAsync(current.Id, CancellationToken.None).ConfigureAwait(true) ?? current;
        IsNew = false;
        Fill(_editing);
        HasProfile = true;
    }

    /// <inheritdoc />
    public override Task ProfileChangedAsync()
    {
        IsNew = false;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Drops the new profile that is not saved yet (its picked photo was never copied) and goes back to the selected profile, or
    /// to an empty new one when there is none.
    /// </summary>
    [RelayCommand]
    public async Task CancelNewAsync()
    {
        if (!IsNew)
        {
            return;
        }

        logger.LogInformation("New profile cancelled");
        _editing = null;
        IsNew = false;
        if (session.Profile is null)
        {
            StartNew();
            return;
        }

        await ActivateAsync().ConfigureAwait(true);
        Status = string.Empty;
    }

    /// <summary>Copies the chosen template into the fields.</summary>
    [RelayCommand]
    public void ApplyTemplate()
    {
        if (SelectedTemplate is null || _editing is null)
        {
            return;
        }

        var draft = new Profile { ContentLanguage = Language?.Value ?? ContentLanguages.English };
        TemplateApplier.Apply(SelectedTemplate.Template, draft);
        Name = draft.Name;
        ConceptText = draft.ConceptText;
        Vertical = draft.Format == VideoFormat.Vertical;
        TargetSeconds = draft.TargetSeconds;
        _editing.TemplateId = draft.TemplateId;
    }

    /// <summary>Saves (refused while a required value is missing).</summary>
    [RelayCommand]
    public async Task SaveAsync()
    {
        if (_editing is null)
        {
            return;
        }

        var problem = Validate();
        if (problem is not null)
        {
            Status = problem;
            return;
        }

        _editing.Name = Name.Trim();
        _editing.ContentLanguage = Language!.Value;
        _editing.Format = Vertical ? VideoFormat.Vertical : VideoFormat.Horizontal;
        _editing.TargetSeconds = (int)TargetSeconds!.Value;
        _editing.ConceptText = ConceptText.Trim();

        // The picked photo is copied BEFORE the row is written. A copy that fails (the picked file moved or deleted meanwhile) or a row that cannot be written stops the save: the files this save made are removed and the photo
        // field is put back, so a new profile stays new (Cancel still drops it without a trace) and the stored photo is still the one the
        // row points at (a later save deletes the right old copy).
        var fresh = new List<string>();
        try
        {
            if (_photoSource is not null)
            {
                _editing.ChannelPhoto = await CopyPhotoAsync(_photoSource, fresh).ConfigureAwait(true);
            }
            else if (_photoRemoved)
            {
                _editing.ChannelPhoto = null;
            }

            await profiles.SaveAsync(_editing, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _editing.ChannelPhoto = _storedPhoto;
            DeleteCopies(fresh);
            logger.LogError(ex, "Profile could not be saved; nothing was written");
            Status = text.Format("error.generic", ex.Message);
            return;
        }

        // The row is saved: from here on the profile exists, whatever else fails.
        var oldPhoto = _storedPhoto;
        _storedPhoto = _editing.ChannelPhoto;
        if (oldPhoto is not null && oldPhoto != _storedPhoto)
        {
            await DeletePhotoFileAsync(oldPhoto, _editing.Id).ConfigureAwait(true);
        }

        _photoSource = null;
        _photoRemoved = false;
        ChannelPhotoPath = StoredPhoto(_editing);
        var wasNew = IsNew;
        IsNew = false;
        string? failure = null;
        try
        {
            await session.SelectProfileAsync(_editing, CancellationToken.None).ConfigureAwait(true);
            if (_profilesChanged is not null)
            {
                await _profilesChanged().ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Profile {Id} saved, but it could not be selected", _editing.Id);
            failure = ex.Message;
        }

        Status = failure is null ? text["common.saved"] : text.Format("error.generic", failure);
        logger.LogInformation("Profile {Id} saved ({State})", _editing.Id, wasNew ? "new" : "changed");
        if (wasNew)
        {
            activity.Add(ActivityLevel.Info, text.Format("profile.created", _editing.Name), text.Format("profile.created", ActivityPlaceholders.Name));
        }
    }

    /// <summary>Opens "Kanaldan esinlen" (0.1.53): a channel's address → a draft of a new profile (and template) to edit and create.</summary>
    [RelayCommand]
    public async Task InspireFromChannelAsync()
    {
        if (inspiration is null)
        {
            return;
        }

        await inspiration.ShowAsync(CreateFromChannelAsync).ConfigureAwait(true);
    }

    /// <summary>
    /// Makes a NEW profile from the edited draft of "Kanaldan esinlen": the form starts a new profile, takes the draft (and its photo, copied
    /// into the library by the save like a picked one) and saves it. Null when it was saved; otherwise why not (the draft stays in the
    /// window, so nothing typed is lost; the page goes back to the selected profile).
    /// </summary>
    public async Task<string?> CreateFromChannelAsync(ChannelProfileDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        StartNew();
        Name = draft.Name;
        Language = Languages.FirstOrDefault(l => l.Value == draft.Language) ?? Language;
        Vertical = draft.Vertical;
        TargetSeconds = draft.TargetSeconds;
        ConceptText = draft.ConceptText;
        if (draft.PhotoFile is { } photo && File.Exists(photo))
        {
            ChooseChannelPhoto(photo);
        }

        await SaveAsync().ConfigureAwait(true);
        if (IsNew)
        {
            // The draft stays in the window (it is shown there and can be fixed and created again); the page goes back to the selected
            // profile, so no unsaved form keeps pointing at the look's temporary photo, which the window deletes when it closes.
            var failure = Status.Length > 0 ? Status : text["inspire.not_created"];
            await CancelNewAsync().ConfigureAwait(true);
            return failure;
        }

        logger.LogInformation("Profile {Id} made from a channel", _editing?.Id);
        return null;
    }

    /// <summary>Chooses a channel photo (kept by Save).</summary>
    [RelayCommand]
    public async Task PickChannelPhotoAsync()
    {
        var file = await dialogs.OpenAsync("profile.photo.pick", "profile.images.filter", ["*.png", "*.jpg", "*.jpeg", "*.webp"]).ConfigureAwait(true);
        if (file is not null)
        {
            ChooseChannelPhoto(file);
        }
    }

    /// <summary>Takes <paramref name="file"/> as the channel photo when it is an image the app can read (copied into the library by Save).</summary>
    public void ChooseChannelPhoto(string file)
    {
        ArgumentException.ThrowIfNullOrEmpty(file);
        try
        {
            _ = ImageSize(file);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Not an image the decoder can read, or the file is locked: the reason is shown and logged; the old photo stays.
            logger.LogWarning(ex, "The channel photo could not be read");
            Status = text.Format("profile.photo.failed", Path.GetFileName(file), ex.Message);
            return;
        }

        _photoSource = file;
        _photoRemoved = false;
        ChannelPhotoPath = file;
        Status = text["profile.photo.save"];
    }

    /// <summary>Removes the channel photo (Save deletes the stored copy).</summary>
    [RelayCommand]
    public void RemoveChannelPhoto()
    {
        _photoSource = null;
        _photoRemoved = _editing?.ChannelPhoto is not null;
        ChannelPhotoPath = null;
        if (_photoRemoved)
        {
            Status = text["profile.photo.save"];
        }
    }

    /// <summary>Deletes the profile after a confirmation.</summary>
    [RelayCommand]
    public async Task DeleteAsync()
    {
        if (_editing is null || IsNew)
        {
            return;
        }

        if (session.WorkRunning)
        {
            // A generation or a video queue belongs to the selected profile; its rows would vanish under it.
            Status = text["profile.delete.busy"];
            return;
        }

        var name = _editing.Name;
        var id = _editing.Id;
        Core.Deletion.DeletionPlan plan;
        try
        {
            plan = await deletion.PlanAsync(id, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "What a profile delete takes could not be counted");
            Status = text.Format("error.generic", ex.Message);
            return;
        }

        var answer = await confirm.AskAsync(text["profile.delete.title"], text.Format("profile.delete.message", name, plan.Concepts, plan.Videos, plan.Documents,
            Localization.ByteSizeText.Of(plan.Bytes, text.Culture)), text["profile.delete.confirm"], danger: true).ConfigureAwait(true);
        if (!answer.Confirmed)
        {
            return;
        }

        try
        {
            // Deleting a profile removes its concepts, queue and video records: a backup of the database is taken first so they can come back.
            if (await backup.CreateBeforeChangeAsync("profile delete", CancellationToken.None).ConfigureAwait(true) is not null)
            {
                activity.Add(ActivityLevel.Info, text["backup.before_change"]);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "The backup before a profile delete failed; the profile was not deleted");
            Status = text.Format("backup.before_change_failed", ex.Message);
            return;
        }

        try
        {
            var result = await deletion.DeleteAsync(id, CancellationToken.None).ConfigureAwait(true);
            if (!result.Complete)
            {
                // Nothing becomes an orphan: the profile stays with what could not be removed, and a second try finishes it.
                var files = string.Join("\n", result.Failed.Take(5).Select(f => "• " + Path.GetFileName(f.Path) + " (" + f.Reason + ")"));
                Status = text.Format("profile.delete.in_use", files);
                activity.Add(ActivityLevel.Error, Status);
                return;
            }

            var freed = Localization.ByteSizeText.Of(result.Freed, text.Culture);
            activity.Add(ActivityLevel.Info, text.Format("profile.deleted", name, freed), text.Format("profile.deleted", ActivityPlaceholders.Name, freed));
            _editing = null;
            HasProfile = false;
            var rest = await profiles.ListAsync(CancellationToken.None).ConfigureAwait(true);
            await session.SelectProfileAsync(rest.FirstOrDefault(), CancellationToken.None).ConfigureAwait(true);
            if (_profilesChanged is not null)
            {
                await _profilesChanged().ConfigureAwait(true);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "The profile could not be deleted");
            Status = text.Format("error.generic", ex.Message);
        }
    }

    private string? Validate()
    {
        if (Name.Trim().Length == 0 || Name.Trim().Length > ProfileLimits.NameMax)
        {
            return text["profile.invalid.name"];
        }

        if (Language is null)
        {
            return text["profile.invalid.language"];
        }

        if (TargetSeconds is null || TargetSeconds < ProfileLimits.SecondsMin || TargetSeconds > ProfileLimits.SecondsMax)
        {
            return text.Format("profile.invalid.seconds", ProfileLimits.SecondsMin, ProfileLimits.SecondsMax);
        }

        return ConceptText.Trim().Length == 0 || ConceptText.Length > ProfileLimits.ConceptTextMax ? text["profile.invalid.concept"] : null;
    }

    private void Fill(Profile profile)
    {
        _photoSource = null;
        _photoRemoved = false;
        _storedPhoto = profile.ChannelPhoto;
        ChannelPhotoPath = StoredPhoto(profile);
        Name = profile.Name;
        Language = Languages.FirstOrDefault(l => l.Value == profile.ContentLanguage) ?? Languages.First(l => l.Value == ContentLanguages.English);
        Vertical = profile.Format == VideoFormat.Vertical;
        TargetSeconds = profile.TargetSeconds;
        ConceptText = profile.ConceptText;
        SelectedTemplate = Templates.FirstOrDefault(t => t.Template.Id == profile.TemplateId);
    }

    private void LoadTemplates()
    {
        var language = text.Language == "tr" ? ContentLanguages.Turkish : ContentLanguages.English;
        var selected = SelectedTemplate?.Template.Id;
        Templates.Clear();
        foreach (var template in templates.All)
        {
            Templates.Add(new TemplateOption(template, TemplateApplier.Pick(template.Category, language) + " → " + TemplateApplier.Pick(template.Name, language)));
        }

        SelectedTemplate = Templates.FirstOrDefault(t => t.Template.Id == selected);
    }

    /// <summary>The full path of the stored channel photo; <c>null</c> when there is none or its record points outside the library.</summary>
    private string? StoredPhoto(Profile profile)
    {
        if (profile.ChannelPhoto is not { } stored)
        {
            return null;
        }

        try
        {
            return LibraryPaths.Resolve(paths.LibraryDirectory, stored);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "The channel photo record points outside the library folder; not shown");
            return null;
        }
    }

    /// <summary>
    /// Copies the photo <paramref name="source"/> into the profile's photo folder in the library; the stored relative path. A file this call
    /// creates is added to <paramref name="fresh"/> (one that is already there, the same content under the same name, is left alone and
    /// not listed: it belongs to the stored row). The copy's name carries the profile's id, so two profiles of the same name never share it
    /// and deleting one never deletes the other's.
    /// </summary>
    private async Task<string> CopyPhotoAsync(string source, List<string> fresh)
    {
        var bytes = await File.ReadAllBytesAsync(source).ConfigureAwait(true);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var directory = Path.Combine(paths.LibraryDirectory, LibraryPaths.SafeName(_editing!.Name, "profile"), PhotoFolder);
        Directory.CreateDirectory(directory);
        var name = _editing.Id.ToString("N") + "-" + hash[..10] + "." + LibraryPaths.SafeName(Path.GetExtension(source).TrimStart('.').ToLowerInvariant(), "png");
        var target = Path.Combine(directory, name);
        if (!File.Exists(target))
        {
            await File.WriteAllBytesAsync(target, bytes).ConfigureAwait(true);
            fresh.Add(target);
        }

        return LibraryPaths.Relative(paths.LibraryDirectory, target);
    }

    /// <summary>Deletes the copies a failed save made (a failure is logged; the save's own error is what is shown).</summary>
    private void DeleteCopies(List<string> fresh)
    {
        foreach (var file in fresh)
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "A copy made by a failed profile save could not be deleted");
            }
        }
    }

    /// <summary>
    /// Deletes a channel photo copy that <paramref name="owner"/> no longer points at, unless another profile still does (a failure is logged
    /// and shown; the profile is saved).
    /// </summary>
    private async Task DeletePhotoFileAsync(string stored, Guid owner)
    {
        try
        {
            if ((await profiles.ListAsync(CancellationToken.None).ConfigureAwait(true)).Any(p => p.Id != owner && p.ChannelPhoto == stored))
            {
                logger.LogInformation("The old channel photo copy is kept: another profile uses it");
                return;
            }

            var file = LibraryPaths.Resolve(paths.LibraryDirectory, stored);
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The old channel photo copy could not be deleted");
            activity.Add(ActivityLevel.Warning, text.Format("profile.photo.delete_failed", ex.Message));
        }
    }

    private static (int Width, int Height) ImageSize(string file)
    {
        using var bitmap = new Avalonia.Media.Imaging.Bitmap(file);
        return (bitmap.PixelSize.Width, bitmap.PixelSize.Height);
    }
}
