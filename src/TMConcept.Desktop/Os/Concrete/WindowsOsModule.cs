using Microsoft.Extensions.DependencyInjection;
using TMConcept.Storage.Keys;

namespace TMConcept.Desktop.Os;

/// <summary>Windows: DPAPI key protection, %LOCALAPPDATA% data folder, the shell's Documents folder.</summary>
public sealed class WindowsOsModule : IOsModule
{
    /// <inheritdoc />
    public bool IsCurrent => OperatingSystem.IsWindows();

    /// <inheritdoc />
    public string DefaultDataDirectory() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TM Concept");

    /// <inheritdoc />
    public string DocumentsDirectory() => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    /// <inheritdoc />
    public void Register(IServiceCollection services) => services.AddSingleton<IKeyProtector, DpapiKeyProtector>();
}
