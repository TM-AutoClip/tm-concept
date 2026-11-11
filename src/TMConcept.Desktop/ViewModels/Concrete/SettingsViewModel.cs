using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using TMConcept.Desktop.Localization;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// Settings page: one tab per registered section. The sections (and what they hold: the model catalog, the key list, tool states) are built
/// the first time the page is shown, not at start.
/// </summary>
public sealed partial class SettingsViewModel : PageViewModelBase
{
    private readonly Localizer _text;
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly Func<IEnumerable<SettingsSectionBase>> _sections;
    private ObservableCollection<SettingsSectionBase>? _built;

    /// <summary>Takes the way to build the sections (they are built on first use).</summary>
    public SettingsViewModel(Func<IEnumerable<SettingsSectionBase>> sections, Localizer text, ILogger<SettingsViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(text);
        _sections = sections;
        _text = text;
        _logger = logger;
        _text.LanguageChanged += (_, _) => RefreshTitles();
    }

    /// <inheritdoc />
    public override string TitleKey => "nav.settings";

    /// <inheritdoc />
    public override int Order => 90;

    /// <summary>Tabs (built on first use; the first one is selected then).</summary>
    public ObservableCollection<SettingsSectionBase> Sections
    {
        get
        {
            if (_built is null)
            {
                _built = new ObservableCollection<SettingsSectionBase>(_sections().OrderBy(s => s.Order));
                RefreshTitles();
                SelectedSection ??= _built.FirstOrDefault();
            }

            return _built;
        }
    }

    /// <summary>Shown tab.</summary>
    [ObservableProperty]
    public partial SettingsSectionBase? SelectedSection { get; set; }

    /// <inheritdoc />
    public override Task ActivateAsync() => ActivateSectionAsync(Sections.Count > 0 ? SelectedSection : null);

    /// <summary>Shows a section by type.</summary>
    public async Task<TSection?> ShowAsync<TSection>()
        where TSection : SettingsSectionBase
    {
        var section = Sections.OfType<TSection>().FirstOrDefault();
        SelectedSection = section;
        await ActivateSectionAsync(section).ConfigureAwait(true);
        return section;
    }

    partial void OnSelectedSectionChanged(SettingsSectionBase? value) => _ = ActivateSectionAsync(value);

    private async Task ActivateSectionAsync(SettingsSectionBase? section)
    {
        if (section is null)
        {
            return;
        }

        try
        {
            await section.ActivateAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Settings section {Section} failed to load", section.GetType().Name);
            section.Status = _text.Format("page.load_failed", ex.Message);
        }
    }

    private void RefreshTitles()
    {
        foreach (var section in _built ?? [])
        {
            section.Title = _text[section.TitleKey];
        }
    }
}
