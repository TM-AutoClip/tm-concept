using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Styles;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// Settings → Templates ("Şablonlar", the developer's 0.1.19 decision): at most <see cref="VideoStyleLimits.Max"/> templates on this
/// computer, not tied to profiles, kept in the encrypted database. Each one says how videos look and end: subtitles, the progress bar (off by
/// default), font, title style and position, colours, the drawing style of scenes without images, motion and the closing call to action.
/// Add, edit (Save / Cancel) and delete (asked first; the template is removed completely, made videos keep their own copy). The Concept to
/// video page picks one; each queued video reads it when it starts.
/// </summary>
public sealed partial class VideoStylesSettingsViewModel(IVideoStyleStore store, Localizer text, IConfirmDialog confirm, IActivityLog activity,
    ILogger<VideoStylesSettingsViewModel> logger) : SettingsSectionBase
{
    private bool _loading;

    /// <inheritdoc />
    public override string TitleKey => "settings.styles";

    /// <inheritdoc />
    public override int Order => 25;

    /// <summary>The templates, by name.</summary>
    public ObservableCollection<VideoStyle> Styles { get; } = [];

    /// <summary>The template shown in the form (nothing while a new one is made).</summary>
    [ObservableProperty]
    public partial VideoStyle? SelectedStyle { get; set; }

    /// <summary>The form; <c>null</c> when nothing is chosen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditor))]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    public partial VideoStyleEditorViewModel? Editor { get; set; }

    /// <summary>A form is open.</summary>
    public bool HasEditor => Editor is not null;

    /// <summary>The open template is stored (it can be deleted).</summary>
    public bool CanDelete => Editor is { IsNew: false };

    /// <summary>"3 / 20 templates".</summary>
    [ObservableProperty]
    public partial string CountText { get; set; } = string.Empty;

    /// <summary>Another template may be added.</summary>
    [ObservableProperty]
    public partial bool CanAdd { get; set; } = true;

    /// <summary>There is no template yet.</summary>
    [ObservableProperty]
    public partial bool Empty { get; set; }

    /// <inheritdoc />
    public override Task ActivateAsync() => LoadAsync(SelectedStyle?.Id ?? (Editor is { IsNew: false } open ? open.Id : null));

    /// <summary>Opens a form for a new template (refused at the limit).</summary>
    [RelayCommand]
    public void Add()
    {
        if (Styles.Count >= VideoStyleLimits.Max)
        {
            Status = text.Format("styles.limit", VideoStyleLimits.Max);
            return;
        }

        _loading = true;
        SelectedStyle = null;
        _loading = false;
        Editor = new VideoStyleEditorViewModel(new VideoStyle { Name = NewName() }, isNew: true, text);
        Status = string.Empty;
    }

    /// <summary>Saves the form (refused while a value is wrong or, for a new template, at the limit).</summary>
    [RelayCommand]
    public async Task SaveAsync()
    {
        if (Editor is not { } editor)
        {
            return;
        }

        var style = editor.ToStyle();
        var problems = VideoStyleRules.Problems(style);
        if (problems.Count > 0)
        {
            Status = string.Join(" ", problems.Select(p => text.Format("style.problem." + p.ToString().ToLowerInvariant(), VideoStyleLimits.NameMax)));
            return;
        }

        try
        {
            await store.SaveAsync(style, CancellationToken.None).ConfigureAwait(true);
        }
        catch (InvalidOperationException ex)
        {
            // The limit (another window or an earlier save filled it): said, nothing stored.
            logger.LogWarning(ex, "Template not saved: the limit of {Max} is reached", VideoStyleLimits.Max);
            Status = text.Format("styles.limit", VideoStyleLimits.Max);
            return;
        }

        logger.LogInformation("Template {Id} saved ({State})", style.Id, editor.IsNew ? "new" : "changed");
        Status = text.Format("styles.saved", style.Name);
        activity.Add(ActivityLevel.Info, Status, text.Format("styles.saved", ActivityPlaceholders.Name));
        await LoadAsync(style.Id).ConfigureAwait(true);
    }

    /// <summary>Puts the form back as the template is stored (a new one is dropped).</summary>
    [RelayCommand]
    public void Revert()
    {
        Editor = SelectedStyle is { } chosen ? new VideoStyleEditorViewModel(chosen, isNew: false, text) : null;
        Status = string.Empty;
    }

    /// <summary>Deletes the open template after a confirmation (completely; videos made with it keep their copy).</summary>
    [RelayCommand]
    public async Task DeleteAsync()
    {
        if (Editor is not { IsNew: false } editor || Styles.FirstOrDefault(s => s.Id == editor.Id) is not { } style)
        {
            return;
        }

        var answer = await confirm.AskAsync(text["styles.delete.title"], text.Format("styles.delete.message", style.Name), text["styles.delete.confirm"],
            danger: true).ConfigureAwait(true);
        if (!answer.Confirmed)
        {
            return;
        }

        await store.DeleteAsync(style.Id, CancellationToken.None).ConfigureAwait(true);
        logger.LogInformation("Template {Id} deleted", style.Id);
        Status = text.Format("styles.deleted", style.Name);
        activity.Add(ActivityLevel.Info, Status, text.Format("styles.deleted", ActivityPlaceholders.Name));
        Editor = null;
        await LoadAsync(null).ConfigureAwait(true);
    }

    partial void OnSelectedStyleChanged(VideoStyle? value)
    {
        if (_loading)
        {
            return;
        }

        if (value is not null)
        {
            Editor = new VideoStyleEditorViewModel(value, isNew: false, text);
            Status = string.Empty;
        }
    }

    private async Task LoadAsync(Guid? select)
    {
        var all = await store.ListAsync(CancellationToken.None).ConfigureAwait(true);
        _loading = true;
        try
        {
            Styles.Clear();
            foreach (var style in all)
            {
                Styles.Add(style);
            }

            SelectedStyle = Styles.FirstOrDefault(s => s.Id == select);
        }
        finally
        {
            _loading = false;
        }

        if (SelectedStyle is { } chosen)
        {
            Editor = new VideoStyleEditorViewModel(chosen, isNew: false, text);
        }
        else if (Editor is { IsNew: false })
        {
            // Deleted meanwhile.
            Editor = null;
        }

        CountText = text.Format("styles.count", Styles.Count, VideoStyleLimits.Max);
        CanAdd = Styles.Count < VideoStyleLimits.Max;
        Empty = Styles.Count == 0;
    }

    private string NewName()
    {
        var name = text["styles.new_name"];
        for (var i = 2; Styles.Any(s => string.Equals(s.Name, name, StringComparison.CurrentCultureIgnoreCase)); i++)
        {
            name = text["styles.new_name"] + " " + i.ToString(text.Culture);
        }

        return name;
    }
}
