using Shouldly;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.ViewModels;
using TMConcept.Storage.Database;

namespace TMConcept.Tests.Desktop;

/// <summary>What the unlock window says about a database this version cannot open, in both languages.</summary>
public sealed class DatabaseVersionTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("tr")]
    public void EachProblemHasItsOwnTextAndOnlyAnUnrecordedDatabaseCanBeSetAside(string language)
    {
        using var h = new DesktopHarness();
        var text = h.Get<Localizer>();
        text.Use(language);

        var unrecorded = UnlockViewModel.Describe(new DatabaseVersionException("x") { Problem = DatabaseVersionProblem.Unrecorded }, text);
        unrecorded.CanSetAside.ShouldBeTrue();
        unrecorded.Text.ShouldBe(text["database.unrecorded"]);
        unrecorded.Text.ShouldContain("0.1.16");

        var newer = UnlockViewModel.Describe(new DatabaseVersionException("x") { Problem = DatabaseVersionProblem.Newer, Step = "Version0120" }, text);
        newer.CanSetAside.ShouldBeFalse("a newer version's database is kept for that version: update the app instead");
        newer.Text.ShouldContain("Version0120");

        var failed = UnlockViewModel.Describe(new DatabaseVersionException("x", new InvalidOperationException("disk full"))
        {
            Problem = DatabaseVersionProblem.UpgradeFailed,
            Step = "Version0118",
        }, text);
        failed.CanSetAside.ShouldBeFalse();
        failed.Text.ShouldContain("Version0118");
        failed.Text.ShouldContain("disk full");
        failed.Text.ShouldContain("before-upgrade");

        // Just after a self-update the previous program is still there: the text says how to go back to it.
        var error = new DatabaseVersionException("x") { Problem = DatabaseVersionProblem.UpgradeFailed, Step = "Version0118" };
        UnlockViewModel.Describe(error, text, Path.Combine("folder", "TMConcept.exe.old")).Text.ShouldContain("TMConcept.exe.old");
        UnlockViewModel.Describe(error, text, null).Text.ShouldBe(UnlockViewModel.Describe(error, text).Text);
    }
}
