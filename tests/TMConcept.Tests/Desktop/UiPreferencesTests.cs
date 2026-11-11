using Shouldly;
using TMConcept.Desktop.Paths;

namespace TMConcept.Tests.Desktop;

public sealed class UiPreferencesTests
{
    [Fact]
    public void DrawingIsOnTheGraphicsCardTheLogIsFoldedUntilChosenAndOnePreferenceNeverResetsAnother()
    {
        using var h = new DesktopHarness();
        var file = h.Get<UiPreferencesFile>();
        file.Read().UseGraphicsCard.ShouldBeTrue("a new install draws with the graphics card");

        file.Change(p => p.UseGraphicsCard = false);
        file.Change(p => p.Language = "tr");

        file.Read().LogOpen.ShouldBeFalse("the activity log starts folded");
        file.Change(p => p.LogOpen = true);

        var read = file.Read();
        read.UseGraphicsCard.ShouldBeFalse();
        read.Language.ShouldBe("tr");
        read.LogOpen.ShouldBeTrue();
    }
}
