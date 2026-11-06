using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Notifications;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Paths;
using TMConcept.Desktop.Startup;

namespace TMConcept.Desktop.ViewModels;

/// <summary>General settings: interface language (changes at once), drawing (after a restart) and the bottom-right notifications.</summary>
public sealed partial class GeneralSettingsViewModel(AppSession session, Localizer text, UiPreferencesFile preferences, INotifier notifier,
    ILogger<GeneralSettingsViewModel> logger)
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

    /// <summary>Draw with the graphics card (stored at once, used from the next start).</summary>
    [ObservableProperty]
    public partial bool UseGraphicsCard { get; set; }

    /// <inheritdoc />
    public override Task ActivateAsync()
    {
        _loading = true;
        UseGraphicsCard = preferences.Read().UseGraphicsCard;
        _loading = false;
        NotificationsEnabled = session.Settings.NotificationsEnabled;
        NotificationSeconds = session.Settings.NotificationSeconds;
        return Task.CompletedTask;
    }

    private bool _loading;

    partial void OnUseGraphicsCardChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        preferences.Change(p => p.UseGraphicsCard = value);
        logger.LogInformation("Setting changed: draw with the {Renderer} after the next start", value ? "graphics card" : "processor");
        Status = text["settings.drawing.restart"];
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
        logger.LogInformation("Setting changed: notifications {State}, shown for {Seconds} s", settings.NotificationsEnabled ? "on" : "off",
            settings.NotificationSeconds);
        Status = text["common.saved"];
    }

    [RelayCommand]
    private void TestNotification() =>
        notifier.Notify(new AppNotification(NotificationKind.Info, "TM Concept", text["settings.notification_test"]));

    private async Task UseLanguageAsync(string code)
    {
        text.Use(code);
        preferences.Change(p => p.Language = code);
        var settings = session.Settings;
        settings.Language = code;
        await session.SaveAsync(settings, CancellationToken.None).ConfigureAwait(true);
        logger.LogInformation("Setting changed: interface language {Language}", code);
        Status = string.Empty;
    }
}
