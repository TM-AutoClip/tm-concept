using Microsoft.Extensions.DependencyInjection;
using TMConcept.Desktop.Composition;
using TMConcept.Desktop.Paths;
using TMConcept.Storage.Crypto;

namespace TMConcept.Tests.Desktop;

/// <summary>The real composition root on a temporary data folder (portable mode, cheap key derivation).</summary>
internal sealed class DesktopHarness : IDisposable
{
    private readonly string _folder;

    public DesktopHarness(Action<IServiceCollection>? overrides = null)
        : this(Path.Combine(Path.GetTempPath(), "tmconcept-tests", "desktop-" + Guid.NewGuid().ToString("N")), overrides)
    {
    }

    /// <summary>On <paramref name="folder"/> (a test that puts files there first); removed at the end.</summary>
    public DesktopHarness(string folder, Action<IServiceCollection>? overrides = null)
    {
        _folder = folder;
        Paths = new AppPaths(_folder, DesktopServices.CurrentOs);
        Services = DesktopServices.Build(Paths, s =>
        {
            s.AddSingleton(KdfParameters.Fast);

            // Nothing reaches a real provider: key tests pass, every other request fails (tests that look at requests replace it).
            s.AddSingleton(new Providers.OfflineHttp().Transport);

            // Never GitHub, never a real download: the update check and the downloads fail at once unless a test gives its own server.
            s.AddSingleton(new TMConcept.Desktop.Updates.UpdateTransport(() => new NoNetworkHandler()));
            s.AddSingleton(new TMConcept.Engines.Tools.DownloadTransport(() => new NoNetworkHandler()));
            s.AddSingleton(new TMConcept.Documents.Web.DocumentTransport(() => new NoNetworkHandler()));
            s.AddSingleton(new TMConcept.Documents.Mcp.McpHttpTransport(() => new NoNetworkHandler()));

            // The graphics card coordinator reads a fixed 16 GB card with 14 GB free and never waits (nothing native, any test machine).
            s.AddSingleton<TMConcept.Core.Hardware.IVramProbe>(new Core.FakeVramProbe(new TMConcept.Core.Hardware.VramReading(16L << 30, 14L << 30, true, "test")));
            s.AddSingleton(Core.TestWaiters.FastOptions);

            // Ollama and LM Studio are not looked for on the test machine's own ports (tests that look for them point at a fake server).
            s.AddSingleton(TMConcept.Providers.Endpoints.EndpointTransport.Network with { DetectAddresses = [] });

            // The app's own Ollama server is never started for real in a test (tests that start it look at the fake launcher).
            s.AddSingleton<TMConcept.Core.AppOllama.IAppOllamaLauncher>(new Core.FakeAppOllamaLauncher());

            // The installed edition is looked for in a folder of the test, never in the test machine's own %LOCALAPPDATA%\TMConcept.
            s.AddSingleton(new TMConcept.Desktop.Updates.InstalledCopy(Microsoft.Extensions.Logging.Abstractions.NullLogger<TMConcept.Desktop.Updates.InstalledCopy>.Instance)
            {
                Root = Path.Combine(folder, "installed-edition"),
            });
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
