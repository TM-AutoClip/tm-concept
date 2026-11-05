using System.Reflection;

namespace TMConcept.Desktop.ViewModels;

/// <summary>About: version, license, third-party notices.</summary>
public sealed class AboutSettingsViewModel : SettingsSectionBase
{
    /// <inheritdoc />
    public override string TitleKey => "settings.about";

    /// <inheritdoc />
    public override int Order => 100;

    /// <summary>Version.</summary>
    public string Version { get; } = typeof(AboutSettingsViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";
}
