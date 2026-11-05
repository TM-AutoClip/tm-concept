using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TMConcept.Core.Notifications;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Paths;
using TMConcept.Desktop.Startup;

namespace TMConcept.Desktop.ViewModels;

/// <summary>General settings: interface language (changes at once) and the bottom-right notifications.</summary>
public sealed partial class GeneralSettingsViewModel(AppSession session, Localizer text, UiPreferencesFile preferences, INotifier notifier)
    : SettingsSectionBase
{
    /// <inheritdoc />
    public override string TitleKey => "settings.general";

    /// <inheritdoc />
    public override int Order => 10;

    /// <summary>Notifications on.</summary>
    [ObservableProperty]
    public partial bool NotificationsEnabled { get; set; }

    /// <summary>How long a notification stays (seconds).</summary>
    [ObservableProperty]
    public partial decimal? NotificationSeconds { get; set; }

    /// <inheritdoc />
    public override Task ActivateAsync()
    {
        NotificationsEnabled = session.Settings.NotificationsEnabled;
        NotificationSeconds = session.Settings.NotificationSeconds;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task TurkishAsync() => UseLanguageAsync("tr");

    [RelayCommand]
    private Task EnglishAsync() => UseLanguageAsync("en");

    [RelayCommand]
    private async Task SaveAsync()
    {
        var settings = session.Settings;
        settings.NotificationsEnabled = NotificationsEnabled;
        settings.NotificationSeconds = (int)Math.Clamp(NotificationSeconds ?? 60, 3, 3600);
        await session.SaveAsync(settings, CancellationToken.None).ConfigureAwait(true);
        Status = text["common.saved"];
    }

    [RelayCommand]
    private void TestNotification() =>
        notifier.Notify(new AppNotification(NotificationKind.Info, "TM Concept", text["settings.notification_test"]));

    private async Task UseLanguageAsync(string code)
    {
        text.Use(code);
        preferences.Write(new UiPreferences { Language = code });
        var settings = session.Settings;
        settings.Language = code;
        await session.SaveAsync(settings, CancellationToken.None).ConfigureAwait(true);
        Status = string.Empty;
    }
}
