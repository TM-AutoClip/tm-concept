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
/// format, target length, the channel's concept, optional sample images with captions and the rights warning, and the model that describes a
/// sample image ("Describe with a model": chosen here, where it is used; kept for the profile). The other models are chosen on the pages
/// that use them. Saving with a required value missing is not possible. Deleting a profile is confirmed and permanent; video files stay on disk.
/// </summary>
public sealed partial class ProfileViewModel(AppSession session, IProfileStore profiles, ITemplateSource templates, IAppPaths paths, FileDialogs dialogs,
    IConfirmDialog confirm, IActivityLog activity, Localizer text, ILogger<ProfileViewModel> logger, StepPickerViewModel picker,
    Core.Images.ImageDescriber describer, BackupService backup) : PageViewModelBase
{
    /// <summary>The model that describes a sample image (this profile's last choice).</summary>
    public StepPickerViewModel Picker { get; } = picker;

    private Profile? _editing;
    private Func<Task>? _profilesChanged;

    /// <inheritdoc />
    public override string TitleKey => "nav.profile";

    /// <inheritdoc />
    public override string Glyph => "👤";

    /// <inheritdoc />
    public override int Order => 50;

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


    /// <summary>Sample images.</summary>
    public ObservableCollection<ProfileImageViewModel> Images { get; } = [];

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
        Images.Clear();
        HasProfile = true;
        Status = string.Empty;
        _ = LoadStepsAsync();
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
        await LoadImagesAsync().ConfigureAwait(true);
        await LoadStepsAsync().ConfigureAwait(true);
        HasProfile = true;
    }

    /// <inheritdoc />
    public override Task ProfileChangedAsync()
    {
        IsNew = false;
        return Task.CompletedTask;
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
        try
        {
            await profiles.SaveAsync(_editing, CancellationToken.None).ConfigureAwait(true);
            foreach (var image in Images)
            {
                image.Image.Caption = image.Caption.Trim();
                await profiles.SaveImageAsync(image.Image, CancellationToken.None).ConfigureAwait(true);
            }

            var wasNew = IsNew;
            IsNew = false;
            await session.SelectProfileAsync(_editing, CancellationToken.None).ConfigureAwait(true);
            if (_profilesChanged is not null)
            {
                await _profilesChanged().ConfigureAwait(true);
            }

            Status = text["common.saved"];
            logger.LogInformation("Profile {Id} saved ({State})", _editing.Id, wasNew ? "new" : "changed");
            if (wasNew)
            {
                activity.Add(ActivityLevel.Info, text.Format("profile.created", _editing.Name), text.Format("profile.created", ActivityPlaceholders.Name));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Profile could not be saved");
            Status = text.Format("error.generic", ex.Message);
        }
    }

    /// <summary>Adds sample images (copied into the profile's image folder).</summary>
    [RelayCommand]
    public async Task AddImagesAsync()
    {
        var files = await dialogs.OpenManyAsync("profile.images.pick", "profile.images.filter", ["*.png", "*.jpg", "*.jpeg", "*.webp"], multiple: true)
            .ConfigureAwait(true);
        await AddImageFilesAsync(files).ConfigureAwait(true);
    }

    /// <summary>Adds the given image files (also used by drag and drop and tests).</summary>
    public async Task AddImageFilesAsync(IReadOnlyList<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (_editing is null || IsNew)
        {
            Status = text["profile.images.save_first"];
            return;
        }

        foreach (var file in files)
        {
            if (Images.Count >= ProfileLimits.ImagesMax)
            {
                Status = text.Format("profile.images.too_many", ProfileLimits.ImagesMax);
                break;
            }

            try
            {
                var bytes = await File.ReadAllBytesAsync(file).ConfigureAwait(true);
                var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
                if (Images.Any(i => i.Image.Sha256 == hash))
                {
                    Status = text["profile.images.duplicate"];
                    continue;
                }

                var (width, height) = ImageSize(file);
                var folder = Path.Combine(paths.LibraryDirectory, LibraryPaths.SafeName(_editing.Name, "profile"), "images");
                Directory.CreateDirectory(folder);
                var target = Path.Combine(folder, hash[..10] + "-" + LibraryPaths.SafeName(Path.GetFileName(file), "image" + Path.GetExtension(file)));
                await File.WriteAllBytesAsync(target, bytes).ConfigureAwait(true);
                var record = new ProfileImage
                {
                    ProfileId = _editing.Id,
                    FileName = LibraryPaths.Relative(paths.LibraryDirectory, target),
                    Width = width,
                    Height = height,
                    Sha256 = hash,
                };
                await profiles.SaveImageAsync(record, CancellationToken.None).ConfigureAwait(true);
                Images.Add(new ProfileImageViewModel(record, target));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Not an image the decoder can read, or the file is locked: the reason is shown and logged.
                logger.LogWarning(ex, "Sample image could not be added");
                Status = text.Format("profile.images.failed", Path.GetFileName(file), ex.Message);
            }
        }
    }

    /// <summary>"Describe with a model": a paid vision request after a confirmation; the caption replaces the image's text.</summary>
    [RelayCommand]
    private async Task DescribeImageAsync(ProfileImageViewModel? image)
    {
        if (image is null)
        {
            return;
        }

        if (Picker.Model(Core.Pipeline.PipelineSteps.DescribeImage) is null)
        {
            Status = text["profile.describe.no_model"];
            return;
        }

        var answer = await confirm.AskAsync(text["profile.describe.title"], text["profile.describe.message"], text["profile.describe.confirm"], danger: false)
            .ConfigureAwait(true);
        if (!answer.Confirmed)
        {
            return;
        }

        // The key gate (a provider without a key gets one asked for) and the profile's choice is saved.
        if (!await Picker.PrepareAsync([Core.Pipeline.PipelineSteps.DescribeImage], "install.purpose.describe", CancellationToken.None).ConfigureAwait(true))
        {
            Status = Picker.Status;
            return;
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(image.FullPath).ConfigureAwait(true);
            var mime = Path.GetExtension(image.FullPath).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".webp" => "image/webp",
                ".gif" => "image/gif",
                _ => "image/jpeg",
            };
            var (caption, cost) = await describer.DescribeAsync(bytes, mime, text.Language == "tr" ? ContentLanguages.Turkish : ContentLanguages.English, _editing?.Id, CancellationToken.None)
                .ConfigureAwait(true);
            image.Caption = caption;
            activity.Add(ActivityLevel.Info, text.Format("profile.describe.done", text.Usd(cost)));
            Status = text.Format("profile.describe.done", text.Usd(cost));
        }
        catch (Core.Models.ModelNotReadyException ex)
        {
            logger.LogWarning("Describe not started: the {Step} step has no {Missing}", ex.Step, ex.Missing);
            Status = text["profile.describe.no_model"];
        }
        catch (Core.Providers.ProviderException ex)
        {
            logger.LogWarning("Describe failed ({Code})", ex.Code);
            Status = text.Format("profile.describe.failed", ProviderErrors.Text(text, ex.Code));
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Describe failed");
            Status = text.Format("error.generic", ex.Message);
        }
    }

    /// <summary>Removes a sample image record (the copied file stays in the folder).</summary>
    [RelayCommand]
    private async Task RemoveImageAsync(ProfileImageViewModel? image)
    {
        if (image is null)
        {
            return;
        }

        await profiles.DeleteImageAsync(image.Image.Id, CancellationToken.None).ConfigureAwait(true);
        Images.Remove(image);
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

        var answer = await confirm.AskAsync(text["profile.delete.title"], text.Format("profile.delete.message", _editing.Name),
            text["profile.delete.confirm"], danger: true).ConfigureAwait(true);
        if (!answer.Confirmed)
        {
            return;
        }

        try
        {
            // Deleting a profile removes its concepts, queue and video records: a backup is taken first so they can come back.
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
            await profiles.DeleteAsync(_editing.Id, CancellationToken.None).ConfigureAwait(true);
            activity.Add(ActivityLevel.Info, text.Format("profile.deleted", _editing.Name), text.Format("profile.deleted", ActivityPlaceholders.Name));
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
        Name = profile.Name;
        Language = Languages.FirstOrDefault(l => l.Value == profile.ContentLanguage) ?? Languages.First(l => l.Value == ContentLanguages.English);
        Vertical = profile.Format == VideoFormat.Vertical;
        TargetSeconds = profile.TargetSeconds;
        ConceptText = profile.ConceptText;
        SelectedTemplate = Templates.FirstOrDefault(t => t.Template.Id == profile.TemplateId);
    }

    private async Task LoadStepsAsync()
    {
        if (_editing is { } profile)
        {
            try
            {
                await Picker.LoadAsync(IsNew ? null : profile.Id, profile.ContentLanguage, Core.Pipeline.PipelineSteps.ProfilePage).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "The profile's models could not be shown");
                Status = text.Format("error.generic", ex.Message);
            }
        }
    }

    private async Task LoadImagesAsync()
    {
        Images.Clear();
        if (_editing is null)
        {
            return;
        }

        foreach (var image in await profiles.ImagesAsync(_editing.Id, CancellationToken.None).ConfigureAwait(true))
        {
            try
            {
                Images.Add(new ProfileImageViewModel(image, LibraryPaths.Resolve(paths.LibraryDirectory, image.FileName)));
            }
            catch (InvalidOperationException ex)
            {
                logger.LogWarning(ex, "A sample image record points outside the library folder; skipped");
            }
        }
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

    private static (int Width, int Height) ImageSize(string file)
    {
        using var bitmap = new Avalonia.Media.Imaging.Bitmap(file);
        return (bitmap.PixelSize.Width, bitmap.PixelSize.Height);
    }
}
