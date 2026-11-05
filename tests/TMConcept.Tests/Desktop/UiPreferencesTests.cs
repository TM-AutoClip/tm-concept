using Shouldly;
using TMConcept.Desktop.Paths;

namespace TMConcept.Tests.Desktop;

public sealed class UiPreferencesTests
{
    [Fact]
    public void DrawingIsOnTheProcessorUntilChosenAndOnePreferenceNeverResetsAnother()
    {
        using var h = new DesktopHarness();
        var file = h.Get<UiPreferencesFile>();
        file.Read().UseGraphicsCard.ShouldBeFalse();

        file.Change(p => p.UseGraphicsCard = true);
        file.Change(p => p.Language = "tr");

        var read = file.Read();
        read.UseGraphicsCard.ShouldBeTrue();
        read.Language.ShouldBe("tr");
    }
}
