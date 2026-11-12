using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Shouldly;
using TMConcept.Desktop.Views;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// Every window carries the app icon (the taskbar never shows the framework's icon), and the owned dialogs do not open their own
/// taskbar button. 0.1.13 first set the icon through property metadata, which threw at start in the real app and was never run here.
/// </summary>
public sealed class WindowIconTests
{
    public static TheoryData<string> Windows() =>
    [
        nameof(MainWindow), nameof(FirstRunWindow), nameof(UnlockWindow), nameof(ConfirmWindow), nameof(ApiKeyWindow), nameof(InstallWindow),
        nameof(ReportProblemWindow), nameof(PlayerWindow),
    ];

    private static Window Create(string name) => name switch
    {
        nameof(MainWindow) => new MainWindow(),
        nameof(FirstRunWindow) => new FirstRunWindow(),
        nameof(UnlockWindow) => new UnlockWindow(),
        nameof(ConfirmWindow) => new ConfirmWindow(),
        nameof(ApiKeyWindow) => new ApiKeyWindow(),
        nameof(InstallWindow) => new InstallWindow(),
        nameof(ReportProblemWindow) => new ReportProblemWindow(),
        nameof(PlayerWindow) => new PlayerWindow(),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    [AvaloniaTheory]
    [MemberData(nameof(Windows))]
    public void EveryWindowShowsTheAppIcon(string name)
    {
        var window = Create(name);
        window.Show();
        try
        {
            window.Icon.ShouldNotBeNull($"{name} would show the framework's icon on the taskbar");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(nameof(ConfirmWindow))]
    [InlineData(nameof(ApiKeyWindow))]
    [InlineData(nameof(InstallWindow))]
    [InlineData(nameof(ReportProblemWindow))]
    public void OwnedDialogsStayOffTheTaskbar(string name) => Create(name).ShowInTaskbar.ShouldBeFalse();
}
