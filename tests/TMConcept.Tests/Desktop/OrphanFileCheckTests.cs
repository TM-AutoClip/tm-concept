using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Activity;
using TMConcept.Desktop.Components;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.DiskSpace;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Paths;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// 0.1.54, the unused model files (the developer: "eski dosyalar silinmiyor, disk şişiyor"): the files of the components folder no component
/// owns (an unfinished download, an older version's folder, an old components folder) are listed with their sizes; the start asks once a day
/// from a size on and deletes them for good only on a yes; Settings → Storage → "Kullanılmayan model dosyalarını temizle" takes the same rows
/// and never a used component. Temporary data folder; nothing is downloaded.
/// </summary>
public sealed class OrphanFileCheckTests
{
    private static (DesktopHarness H, FakeConfirm Confirm) Setup()
    {
        var confirm = new FakeConfirm();
        var h = new DesktopHarness(s => s.AddSingleton<IConfirmDialog>(confirm));
        Directory.CreateDirectory(Path.Combine(h.Paths.ToolsDirectory, "downloads"));
        File.WriteAllBytes(Path.Combine(h.Paths.ToolsDirectory, "downloads", "model.part"), new byte[4096]);
        Directory.CreateDirectory(Path.Combine(h.Paths.ToolsDirectory, "sd-cpp", "master-0999-old"));
        File.WriteAllBytes(Path.Combine(h.Paths.ToolsDirectory, "sd-cpp", "master-0999-old", "sd-cli.exe"), new byte[2048]);
        Directory.CreateDirectory(Path.Combine(h.Paths.DataDirectory, "tools.old-20261125-134716", "images"));
        File.WriteAllBytes(Path.Combine(h.Paths.DataDirectory, "tools.old-20261125-134716", "images", "weights.gguf"), new byte[8192]);
        return (h, confirm);
    }

    private static OrphanFileCheck Check(DesktopHarness h, long from) => new(h.Get<IDiskCleaner>(), h.Get<IConfirmDialog>(), h.Get<UiPreferencesFile>(),
        h.Get<IComponentWorkGate>(), h.Get<AppSession>(), h.Get<IActivityLog>(), h.Get<Localizer>(), TimeProvider.System, NullLogger<OrphanFileCheck>.Instance)
    {
        AskFromBytes = from,
    };

    [AvaloniaFact]
    public async Task TheStartListsTheUnusedFilesWithTheirSizesAndDeletesThemOnlyOnAYesOnceADay()
    {
        var (h, confirm) = Setup();
        using var _ = h;
        await FirstRunTests.CreateAsync(h);
        var text = h.Get<Localizer>();

        var freed = await Check(h, from: 1).RunAsync(CancellationToken.None);

        confirm.Asked.ShouldBe([text["storage.orphans.start.title"]]);
        var message = confirm.Messages.ShouldHaveSingleItem();
        message.ShouldContain("tools.old-20261125-134716");
        message.ShouldContain(StorageSettingsViewModel.Size(8192, text.Culture));
        confirm.Dangers.ShouldBe([true]);
        freed.ShouldBeGreaterThanOrEqualTo(4096 + 2048 + 8192);
        File.Exists(Path.Combine(h.Paths.ToolsDirectory, "downloads", "model.part")).ShouldBeFalse();
        Directory.Exists(Path.Combine(h.Paths.ToolsDirectory, "sd-cpp", "master-0999-old")).ShouldBeFalse();
        Directory.Exists(Path.Combine(h.Paths.DataDirectory, "tools.old-20261125-134716")).ShouldBeFalse();
        h.Get<UiPreferencesFile>().Read().OrphansAskedAtUtc.ShouldNotBeNull();

        // Asked at most once a day.
        Directory.CreateDirectory(Path.Combine(h.Paths.ToolsDirectory, "downloads"));
        File.WriteAllBytes(Path.Combine(h.Paths.ToolsDirectory, "downloads", "again.part"), new byte[4096]);
        (await Check(h, from: 1).RunAsync(CancellationToken.None)).ShouldBe(0);
        confirm.Asked.Count.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task ANoKeepsEverythingAndBelowTheSizeNothingIsAsked()
    {
        var (h, confirm) = Setup();
        using var _ = h;
        await FirstRunTests.CreateAsync(h);

        (await Check(h, from: OrphanFileCheck.DefaultAskFromBytes).RunAsync(CancellationToken.None)).ShouldBe(0);
        confirm.Asked.ShouldBeEmpty("14 KB is far below 500 MB");

        confirm.Answer = false;
        (await Check(h, from: 1).RunAsync(CancellationToken.None)).ShouldBe(0);
        confirm.Asked.ShouldHaveSingleItem();
        File.Exists(Path.Combine(h.Paths.ToolsDirectory, "downloads", "model.part")).ShouldBeTrue();
        Directory.Exists(Path.Combine(h.Paths.DataDirectory, "tools.old-20261125-134716")).ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task TheStorageButtonTakesOnlyTheFilesNoComponentOwns()
    {
        var (h, confirm) = Setup();
        using var _ = h;
        await FirstRunTests.CreateAsync(h);
        var report = await h.Get<IDiskCleaner>().ReportAsync(CancellationToken.None);

        var orphans = DiskSelection.Orphans(report.Items);

        orphans.ShouldNotBeEmpty();
        orphans.ShouldAllBe(o => o.State == TMConcept.Core.DiskSpace.DiskItemState.Leftover);
        orphans.ShouldNotContain(o => o.Key.StartsWith("work:", StringComparison.Ordinal), "work caches are not model files");
        orphans.ShouldNotContain(o => o.Key == "tools:unknown-image-models", "an image model another version installed goes only when ticked");

        var page = h.Get<StorageSettingsViewModel>();
        await page.RefreshAsync();
        await page.CleanOrphansCommand.ExecuteAsync(null);

        confirm.Asked.ShouldBe([h.Get<Localizer>()["storage.orphans.title"]]);
        Directory.Exists(Path.Combine(h.Paths.DataDirectory, "tools.old-20261125-134716")).ShouldBeFalse();
    }
}
