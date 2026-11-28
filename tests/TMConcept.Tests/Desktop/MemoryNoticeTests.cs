using Shouldly;
using TMConcept.Core.Activity;
using TMConcept.Core.Models;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Memory;
using TMConcept.Desktop.Startup;

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

    [Fact]
    public void AModelThatWouldSpillAsksWithFreeAndNeededMemoryAndSaysSoWhenTriedAnyway()
    {
        // 0.1.59 (K24-a): a model in a process of its own that does not fit is not started; the question names free and needed memory.
        var text = new Localizer();
        text.Use("tr");
        var dialog = new MemoryShortfallDialog(new WindowHost(), text, new SilentNotifier());
        var spill = new MemoryShortfall("YuE", 6 * GiB, (long)(3.8 * GiB), "PDH", 6 * GiB, 20 * GiB, 16 * GiB) { CanTryAnyway = true };

        var message = dialog.Message(spill);
        message.ShouldContain("kartta 3,8 GB boş, bu model ~6 GB ister");
        message.ShouldContain("Tekrar dene");
        message.ShouldContain("daha küçük bir model seç");
        dialog.Message(spill with { CanTryAnyway = false }).ShouldNotContain("~6 GB ister", Case.Sensitive, "the other models keep the full figures");

        var activity = new Lines();
        new MemoryNoticeLog(activity, text).Report(new MemoryNotice(MemoryNoticeKind.Placed, "YuE", "local/yue-v1-exl2") { Placement = MemoryPlacement.SharedMemory });
        activity.Added.ShouldHaveSingleItem().Text.ShouldContain("paylaşılan belleği");
    }

    private sealed class SilentNotifier : TMConcept.Core.Notifications.INotifier
    {
        public void Notify(TMConcept.Core.Notifications.AppNotification notification)
        {
        }
    }

    private sealed class Lines : IActivityLog
    {
        public List<(ActivityLevel Level, string Text)> Added { get; } = [];

        public void Add(ActivityLevel level, string text) => Added.Add((level, text));

        public void Add(ActivityLevel level, string text, string fileText) => Added.Add((level, text));
    }
}
