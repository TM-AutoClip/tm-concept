using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace TMConcept.Desktop.Paths;

/// <summary>Reads and writes <see cref="UiPreferences"/>.</summary>
public sealed class UiPreferencesFile(AppPaths paths, ILogger<UiPreferencesFile> logger)
{
    /// <summary>The preferences (defaults when the file is missing or unreadable; the reason is logged).</summary>
    public UiPreferences Read()
    {
        if (!File.Exists(paths.PreferencesFile))
        {
            return new UiPreferences();
        }

        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(paths.PreferencesFile), DesktopJsonContext.Default.UiPreferences) ?? new UiPreferences();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            logger.LogWarning(ex, "Preferences file could not be read; defaults are used");
            return new UiPreferences();
        }
    }

    /// <summary>Changes the stored preferences (read, change, write), so one preference never resets another.</summary>
    public void Change(Action<UiPreferences> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var preferences = Read();
        change(preferences);
        Write(preferences);
    }

    /// <summary>Writes the preferences.</summary>
    public void Write(UiPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        File.WriteAllText(paths.PreferencesFile, JsonSerializer.Serialize(preferences, DesktopJsonContext.Default.UiPreferences));
        logger.LogInformation("Preferences saved (language {Language}, graphics card {GraphicsCard}, log open {LogOpen})",
            preferences.Language.Length > 0 ? preferences.Language : "not chosen", preferences.UseGraphicsCard, preferences.LogOpen);
    }
}
