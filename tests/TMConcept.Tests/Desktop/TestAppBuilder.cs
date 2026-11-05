using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(TMConcept.Tests.Desktop.TestAppBuilder))]

namespace TMConcept.Tests.Desktop;

/// <summary>Headless Avalonia with the app's styles (no services: the startup flow does not run).</summary>
public static class TestAppBuilder
{
    /// <summary>Builder.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TMConcept.Desktop.App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
