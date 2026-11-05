using Microsoft.Extensions.DependencyInjection;
using TMConcept.Desktop.Composition;
using TMConcept.Desktop.Paths;
using TMConcept.Storage.Crypto;

namespace TMConcept.Tests.Desktop;

/// <summary>The real composition root on a temporary data folder (portable mode, cheap key derivation).</summary>
internal sealed class DesktopHarness : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "tmconcept-tests", "desktop-" + Guid.NewGuid().ToString("N"));

    public DesktopHarness(Action<IServiceCollection>? overrides = null)
    {
        Paths = new AppPaths(_folder, DesktopServices.CurrentOs);
        Services = DesktopServices.Build(Paths, s =>
        {
            s.AddSingleton(KdfParameters.Fast);
            overrides?.Invoke(s);
        });
    }

    public AppPaths Paths { get; }

    public ServiceProvider Services { get; }

    public T Get<T>()
        where T : notnull => Services.GetRequiredService<T>();

    public void Dispose()
    {
        Services.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // Windows may still hold a file for a moment; the temp folder is cleaned later.
        }
    }
}
