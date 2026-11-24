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
/// video page picks one; each queued video reads it when it starts. Unsaved changes survive a page switch, and opening another template asks
/// before they are dropped. A database error is logged and shown, never left to the dispatcher.
/// </summary>
public sealed partial class VideoStylesSettingsViewModel(IVideoStyleStore store, IVideoPaletteStore palettes, Localizer text, IConfirmDialog confirm,
    IActivityLog activity, ILogger<VideoStylesSettingsViewModel> logger) : SettingsSectionBase
{
    private bool _loading;
    private IReadOnlyList<SavedPalette> _saved = [];
    private bool _fontsLogged;

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
    public override Task ActivateAsync() => LoadSafeAsync(SelectedStyle?.Id ?? (Editor is { IsNew: false } open ? open.Id : null));

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
        // 0.1.33: a new template is drawn by the app from the scene plan; the model's free code stays a choice.
        Editor = NewEditor(new VideoStyle { Name = NewName(), Drawer = SceneDrawer.App, Finish = true }, true);
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
        catch (VideoStyleLimitException ex)
        {
            // The limit (another window or an earlier save filled it): said, nothing stored.
            logger.LogWarning(ex, "Template not saved: the limit of {Max} is reached", VideoStyleLimits.Max);
            Status = text.Format("styles.limit", VideoStyleLimits.Max);
            return;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The database (busy during a backup, disk full, being restored): the form stays as it is, so nothing typed is lost.
            logger.LogError(ex, "Template {Id} could not be saved", style.Id);
            Status = text.Format("error.generic", ex.Message);
            return;
        }

        logger.LogInformation("Template {Id} saved ({State})", style.Id, editor.IsNew ? "new" : "changed");
        Status = text.Format("styles.saved", style.Name);
        activity.Add(ActivityLevel.Info, Status, text.Format("styles.saved", ActivityPlaceholders.Name));
        await LoadSafeAsync(style.Id).ConfigureAwait(true);
    }

    /// <summary>Puts the form back as the template is stored (a new one is dropped).</summary>
    [RelayCommand]
    public void Revert()
    {
        Editor = SelectedStyle is { } chosen ? NewEditor(chosen, false) : null;
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

        try
        {
            await store.DeleteAsync(style.Id, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Template {Id} could not be deleted", style.Id);
            Status = text.Format("error.generic", ex.Message);
            return;
        }

        logger.LogInformation("Template {Id} deleted", style.Id);
        Status = text.Format("styles.deleted", style.Name);
        activity.Add(ActivityLevel.Info, Status, text.Format("styles.deleted", ActivityPlaceholders.Name));
        Editor = null;
        await LoadSafeAsync(null).ConfigureAwait(true);
    }

    /// <summary>The last switch to another template in the list (its question may still be open); awaited by tests.</summary>
    public Task Switching { get; private set; } = Task.CompletedTask;

    partial void OnSelectedStyleChanged(VideoStyle? oldValue, VideoStyle? newValue)
    {
        if (_loading || newValue is null)
        {
            return;
        }

        if (Editor is { } open && open.Id != newValue.Id && open.Changed)
        {
            // Unsaved changes of the open template (or of a new one): asked before they are dropped.
            Switching = SwitchAsync(oldValue, newValue);
            return;
        }

        Editor = NewEditor(newValue, false);
        Status = string.Empty;
    }

    /// <summary>Asks before the unsaved form is dropped; No keeps the form and puts the list's selection back.</summary>
    private async Task SwitchAsync(VideoStyle? previous, VideoStyle next)
    {
        try
        {
            var answer = await confirm.AskAsync(text["styles.discard.title"], text["styles.discard.message"], text["styles.discard.confirm"], danger: true)
                .ConfigureAwait(true);
            if (answer.Confirmed)
            {
                if (SelectedStyle?.Id == next.Id)
                {
                    Editor = NewEditor(next, false);
                    Status = string.Empty;
                }

                return;
            }

            _loading = true;
            try
            {
                SelectedStyle = previous;
            }
            finally
            {
                _loading = false;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Switching templates failed");
            Status = text.Format("error.generic", ex.Message);
        }
    }

    /// <summary><see cref="LoadAsync"/> with its error logged and shown (a template that cannot be read, a database that is busy).</summary>
    private async Task LoadSafeAsync(Guid? select)
    {
        try
        {
            await LoadAsync(select).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "The templates could not be loaded");
            Status = text.Format("error.generic", ex.Message);
        }
    }

    private async Task LoadAsync(Guid? select)
    {
        // The fonts installed on this computer are read once, off the interface thread (the editor lists them after the shipped ones).
        var installed = await Task.Run(() => SystemFonts.Installed).ConfigureAwait(true);
        if (!_fontsLogged)
        {
            _fontsLogged = true;
            logger.LogInformation("{Count} installed font families can be used in templates", installed.Count);
        }

        _saved = await palettes.ListAsync(CancellationToken.None).ConfigureAwait(true);
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
            // Coming back to the page keeps unsaved changes of the same template (the form is rebuilt only when it says what is stored).
            if (Editor is not { IsNew: false } open || open.Id != chosen.Id || !open.Changed)
            {
                Editor = NewEditor(chosen, false);
            }
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

    /// <summary>
    /// Saves the form's own colours as a named palette (refused while they cannot be read or the name is missing; at the limit). The form
    /// then shows the saved palette in its list, chosen.
    /// </summary>
    [RelayCommand]
    public async Task SavePaletteAsync()
    {
        if (Editor is not { } editor)
        {
            return;
        }

        var name = editor.PaletteName.Trim();
        if (name.Length == 0 || name.Length > VideoStyleLimits.NameMax)
        {
            Status = text.Format("styles.own.name_needed", VideoStyleLimits.NameMax);
            return;
        }

        if (editor.OwnPalette() is not { } colours || !PaletteDesigner.Check(colours).Ok)
        {
            Status = editor.PaletteStatus;
            return;
        }

        var saved = new SavedPalette { Id = editor.SavedPaletteId ?? Guid.NewGuid(), Name = name, Colors = colours };
        try
        {
            await palettes.SaveAsync(saved, CancellationToken.None).ConfigureAwait(true);
            _saved = await palettes.ListAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (VideoStyleLimitException ex)
        {
            logger.LogWarning(ex, "Palette not saved: the limit of {Max} is reached", VideoStyleLimits.PalettesMax);
            Status = text.Format("styles.own.limit", VideoStyleLimits.PalettesMax);
            return;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Palette {Id} could not be saved", saved.Id);
            Status = text.Format("error.generic", ex.Message);
            return;
        }

        logger.LogInformation("Palette {Id} saved", saved.Id);
        editor.SetSaved(_saved, saved.Id);
        Status = text.Format("styles.own.saved", name);
    }

    /// <summary>Deletes the chosen saved palette after a confirmation (templates that use it keep their own copy of the colours).</summary>
    [RelayCommand]
    public async Task DeletePaletteAsync()
    {
        if (Editor is not { SavedPaletteId: { } id } editor || _saved.FirstOrDefault(p => p.Id == id) is not { } palette)
        {
            return;
        }

        var answer = await confirm.AskAsync(text["styles.own.delete.title"], text.Format("styles.own.delete.message", palette.Name),
            text["styles.own.delete"], danger: true).ConfigureAwait(true);
        if (!answer.Confirmed)
        {
            return;
        }

        try
        {
            await palettes.DeleteAsync(id, CancellationToken.None).ConfigureAwait(true);
            _saved = await palettes.ListAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Palette {Id} could not be deleted", id);
            Status = text.Format("error.generic", ex.Message);
            return;
        }

        logger.LogInformation("Palette {Id} deleted", id);
        editor.SetSaved(_saved, null);
        Status = text.Format("styles.own.deleted", palette.Name);
    }

    private VideoStyleEditorViewModel NewEditor(VideoStyle style, bool isNew) => new(style, isNew, text, _saved);

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
