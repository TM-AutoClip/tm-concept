using Microsoft.Extensions.DependencyInjection;
using TMConcept.Storage.Keys;

namespace TMConcept.Desktop.Os;

/// <summary>
/// Linux: Secret Service key protection (secret-tool; without a keyring the app asks for a master password), $XDG_DATA_HOME data folder and
/// the XDG documents folder (<c>XDG_DOCUMENTS_DIR</c> in user-dirs.dirs, otherwise ~/Documents).
/// </summary>
public sealed class LinuxOsModule : IOsModule
{
    /// <inheritdoc />
    public bool IsCurrent => OperatingSystem.IsLinux();

    /// <inheritdoc />
    public string DefaultDataDirectory()
    {
        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var root = string.IsNullOrEmpty(xdg) ? Path.Combine(Home(), ".local", "share") : xdg;
        return Path.Combine(root, "tm-concept");
    }

    /// <inheritdoc />
    public string DocumentsDirectory() => XdgDocuments(Home(), Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"));

    /// <inheritdoc />
    public void Register(IServiceCollection services)
    {
        services.AddSingleton<ICommandRunner, ProcessCommandRunner>();
        services.AddSingleton<IKeyProtector, SecretServiceKeyProtector>();
        services.AddSingleton<IRecycleBin, LinuxRecycleBin>();
        services.AddSingleton<Core.Tools.IToolPlatform, Engines.Tools.LinuxToolPlatform>();
    }

    /// <summary>The documents folder from user-dirs.dirs (<c>XDG_DOCUMENTS_DIR="$HOME/Belgeler"</c>), otherwise ~/Documents.</summary>
    internal static string XdgDocuments(string home, string? configHome)
    {
        var file = Path.Combine(string.IsNullOrEmpty(configHome) ? Path.Combine(home, ".config") : configHome, "user-dirs.dirs");
        if (File.Exists(file))
        {
            foreach (var line in File.ReadLines(file))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("XDG_DOCUMENTS_DIR=", StringComparison.Ordinal))
                {
                    continue;
                }

                var value = trimmed["XDG_DOCUMENTS_DIR=".Length..].Trim('"').Replace("$HOME", home, StringComparison.Ordinal);
                if (Path.IsPathRooted(value) && Path.GetFullPath(value) != Path.GetFullPath(home))
                {
                    return value;
                }
            }
        }

        return Path.Combine(home, "Documents");
    }

    private static string Home() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}
