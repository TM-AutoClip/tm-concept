using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using TMConcept.Desktop.Localization;

namespace TMConcept.Desktop.ViewModels;

/// <summary>Settings page: one tab per registered section.</summary>
public sealed partial class SettingsViewModel : PageViewModelBase
{
    private readonly Localizer _text;
    private readonly ILogger<SettingsViewModel> _logger;

    /// <summary>Collects the sections.</summary>
    public SettingsViewModel(IEnumerable<SettingsSectionBase> sections, Localizer text, ILogger<SettingsViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(text);
        _text = text;
        _logger = logger;
        Sections = new ObservableCollection<SettingsSectionBase>(sections.OrderBy(s => s.Order));
        RefreshTitles();
        _text.LanguageChanged += (_, _) => RefreshTitles();
        SelectedSection = Sections.FirstOrDefault();
    }

    /// <inheritdoc />
    public override string TitleKey => "nav.settings";

    /// <inheritdoc />
    public override string Glyph => "⚙";

    /// <inheritdoc />
    public override int Order => 90;

    /// <summary>Tabs.</summary>
    public ObservableCollection<SettingsSectionBase> Sections { get; }

    /// <summary>Shown tab.</summary>
    [ObservableProperty]
    public partial SettingsSectionBase? SelectedSection { get; set; }

    /// <inheritdoc />
    public override Task ActivateAsync() => ActivateSectionAsync(SelectedSection);

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
        foreach (var section in Sections)
        {
            section.Title = _text[section.TitleKey];
        }
    }
}
