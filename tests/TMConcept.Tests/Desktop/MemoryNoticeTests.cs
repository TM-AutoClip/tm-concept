using Shouldly;
using TMConcept.Core.Activity;
using TMConcept.Core.Models;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Memory;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// The memory check's lines in the activity panel (Turkish, informal): the measuring steps as information, a placement off the card in yellow
/// once per check, a night mode failure in red with its figures. No window, no graphics card.
/// </summary>
public sealed class MemoryNoticeTests
{
    private const long GiB = 1024L * 1024 * 1024;

    [Fact]
    public void EveryStepIsALineAndASlowerPlacementIsYellowOncePerCheck()
    {
        var text = new Localizer();
        text.Use("tr");
        var activity = new Lines();
        var log = new MemoryNoticeLog(activity, text);

        log.Report(new MemoryNotice(MemoryNoticeKind.Measuring, "Z-Image Turbo", "local/z-image-turbo") { Step = 1, Of = 2 });
        log.Report(new MemoryNotice(MemoryNoticeKind.Placed, "Z-Image Turbo", "local/z-image-turbo") { Placement = MemoryPlacement.Disk });
        // An image loop places the model before every picture: said once.
        log.Report(new MemoryNotice(MemoryNoticeKind.Placed, "Z-Image Turbo", "local/z-image-turbo") { Placement = MemoryPlacement.Disk });
        log.Report(new MemoryNotice(MemoryNoticeKind.ProcessorFallback, "Z-Image Turbo", "local/z-image-turbo"));
        log.Report(new MemoryNotice(MemoryNoticeKind.Failed, "Z-Image Turbo", "local/z-image-turbo")
        {
            Shortfall = new MemoryShortfall("Z-Image Turbo", 6 * GiB, (long)(1.5 * GiB), "PDH", (long)(2.3 * GiB), 2 * GiB, 4 * GiB),
        });

        activity.Added.Select(a => a.Level).ShouldBe([ActivityLevel.Info, ActivityLevel.Warning, ActivityLevel.Warning, ActivityLevel.Error]);
        activity.Added[0].Text.ShouldBe("Z-Image Turbo: ekran kartı belleği ölçülüyor (1/2)…");
        activity.Added[1].Text.ShouldContain("ekran kartına sığmadı");
        activity.Added[1].Text.ShouldContain("diskten");
        activity.Added[2].Text.ShouldContain("işlemciyle");
        activity.Added[3].Text.ShouldContain("1,5 / 6 GB boş, 2,3 GB gerekiyor");
        activity.Added[3].Text.ShouldContain("gece modu");
    }

    private sealed class Lines : IActivityLog
    {
        public List<(ActivityLevel Level, string Text)> Added { get; } = [];

        public void Add(ActivityLevel level, string text) => Added.Add((level, text));

        public void Add(ActivityLevel level, string text, string fileText) => Added.Add((level, text));
    }
}
