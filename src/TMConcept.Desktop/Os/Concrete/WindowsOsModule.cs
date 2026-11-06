using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using TMConcept.Storage.Keys;

namespace TMConcept.Desktop.Os;

/// <summary>
/// Windows: DPAPI key protection, %LOCALAPPDATA% data folder, the shell's Documents folder, WaveOut sound for the player, a job object that
/// ends child processes with the app.
/// </summary>
public sealed class WindowsOsModule : IOsModule
{
    /// <inheritdoc />
    public bool IsCurrent => OperatingSystem.IsWindows();

    /// <inheritdoc />
    public string DefaultDataDirectory() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TM Concept");

    /// <inheritdoc />
    public string DocumentsDirectory() => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    /// <inheritdoc />
    public ProcessStartInfo RevealCommand(string file) =>
        // Explorer reads "/select," and the quoted path as one argument; it opens the folder with the file selected.
        new("explorer.exe", "/select,\"" + file + "\"") { UseShellExecute = false };

    /// <inheritdoc />
    public void Register(IServiceCollection services)
    {
        services.AddSingleton<IKeyProtector, DpapiKeyProtector>();
        services.AddSingleton<IRecycleBin, WindowsRecycleBin>();
        services.AddSingleton<Core.Tools.IToolPlatform, Engines.Tools.WindowsToolPlatform>();
        services.AddSingleton<Core.Playback.IAudioOutputFactory, Playback.AudioOutputFactory<Playback.WaveOutAudioOutput>>();
        services.AddSingleton<Engines.Processes.ChildProcessGuardBase, Engines.Processes.WindowsChildProcessGuard>();
    }
}
