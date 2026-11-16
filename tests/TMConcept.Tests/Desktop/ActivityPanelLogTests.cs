using Microsoft.Extensions.Logging;
using Shouldly;
using TMConcept.Core.Activity;
using TMConcept.Desktop.Logging;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// Warnings and errors of every logger reach the activity panel (0.1.16: sd-cli's failure was only in the log file): one line, masked, not
/// written to the log again, never looping through the activity log's own lines, and not twice when code already said the same.
/// </summary>
public sealed class ActivityPanelLogTests
{
    private const string EngineCategory = "TMConcept.Engines.Images.SdCppImageEngine";

    [Fact]
    public void WarningsAndErrorsOfAnyLoggerReachThePanelAsOneShortLineInformationDoesNot()
    {
        using var h = new DesktopHarness();
        var feed = h.Get<IActivityFeed>();
        var logger = h.Get<ILoggerFactory>().CreateLogger(EngineCategory);
        var before = feed.Recent().Count;

        logger.LogInformation("Scene image {Number} made", 1);
        feed.Recent().Count.ShouldBe(before, "information stays in the log file");

        logger.LogWarning("sd-cli.exe ended with exit code {Code} after {Ms} ms", -1073741515, 28583);
        logger.LogError(new InvalidOperationException("z-image-turbo could not make the image\nat line two"), "z-image-turbo: sd-cli ended with code {Code}",
            -1073741515);

        var lines = feed.Recent().Skip(before).ToList();
        lines.Count.ShouldBe(2);
        lines[0].IsWarning.ShouldBeTrue();
        lines[0].Text.ShouldBe("SdCppImageEngine: sd-cli.exe ended with exit code -1073741515 after 28583 ms");
        lines[1].IsError.ShouldBeTrue("an error keeps its red colour");
        lines[1].Text.ShouldBe("SdCppImageEngine: z-image-turbo: sd-cli ended with code -1073741515 — InvalidOperationException: z-image-turbo could not make the image at line two");
        lines[1].Text.ShouldNotContain("\n");
    }

    [Fact]
    public void TheActivityLogsOwnLinesAndAHandlerThatLogsDoNotLoop()
    {
        using var h = new DesktopHarness();
        var feed = h.Get<IActivityFeed>();
        var activity = h.Get<IActivityLog>();
        var logger = h.Get<ILoggerFactory>().CreateLogger(EngineCategory);
        var before = feed.Recent().Count;

        // The activity log writes its line to the log file as a warning: that is not forwarded back.
        activity.Add(ActivityLevel.Warning, "Narration is off for this video");
        feed.Recent().Count.ShouldBe(before + 1);

        // A handler of the panel that logs a warning for every line: the forwarded line does not come back through it again.
        var handled = 0;
        feed.Added += (_, _) =>
        {
            if (++handled < 50)
            {
                logger.LogWarning("Handler saw a line number {Count}", handled);
            }
        };
        activity.Add(ActivityLevel.Error, "Something failed on the page");
        handled.ShouldBeLessThan(5);
        ActivityLoggerProvider.SkippedCategory.ShouldBe(typeof(ActivityLog).FullName);
    }

    [Fact]
    public void PathsHomeEmailsAndKeysAreMaskedAndLongLinesAreCut()
    {
        using var h = new DesktopHarness();
        var feed = h.Get<IActivityFeed>();
        var logger = h.Get<ILoggerFactory>().CreateLogger(EngineCategory);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        logger.LogWarning("Could not read {File} for {Mail} with key {Key}", Path.Combine(home, "private", "notes.txt"), "someone@example.org",
            "sk-ant-" + new string('Z', 24));
        logger.LogError("Long: {Text}", string.Join(' ', Enumerable.Repeat("word", 300)));

        var lines = feed.Recent();
        var masked = lines[^2].Text;
        masked.ShouldNotContain(home);
        masked.ShouldContain("~");
        masked.ShouldNotContain("someone@example.org");
        masked.ShouldNotContain(new string('Z', 24));
        lines[^1].Text.Length.ShouldBe(ActivityLoggerProvider.MaxLength);
        lines[^1].Text.ShouldEndWith("…");
    }

    [Fact]
    public void ALineCodeAlreadyShowedOrTheSameLogLineAgainIsNotRepeated()
    {
        using var h = new DesktopHarness();
        var feed = h.Get<IActivityFeed>();
        var activity = h.Get<IActivityLog>();
        var logger = h.Get<ILoggerFactory>().CreateLogger(EngineCategory);

        activity.Add(ActivityLevel.Error, "Video failed: Greek fire: Scene images could not be made. sd-cli could not load a library");
        var before = feed.Recent().Count;

        logger.LogError("sd-cli could not load a library");
        logger.LogWarning("The same warning twice in a row");
        logger.LogWarning("The same warning twice in a row");

        var lines = feed.Recent().Skip(before).ToList();
        lines.ShouldHaveSingleItem().Text.ShouldBe("SdCppImageEngine: The same warning twice in a row");
    }
}
