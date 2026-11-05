using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TMConcept.Core.Paths;
using TMConcept.Core.Profiles;
using TMConcept.Core.Templates;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Tests.Desktop;

public sealed class ProfileTests
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAACXBIWXMAAAABAAAAAQBPJcTWAAAAEElEQVR4nGP8ywACLGCSAQANEQED1LYyQAAAAABJRU5ErkJggg==");

    [AvaloniaFact]
    public async Task TemplateFillsANewProfileThatIsSavedAndSelected()
    {
        var confirm = new FakeConfirm();
        using var h = new DesktopHarness(s => s.AddSingleton<IConfirmDialog>(confirm));
        await FirstRunTests.CreateAsync(h);
        var page = h.Get<ProfileViewModel>();
        page.StartNew();
        page.Language = page.Languages.First(l => l.Value == ContentLanguages.Turkish);
        await page.SaveAsync();
        page.Status.ShouldNotBe(h.Get<TMConcept.Desktop.Localization.Localizer>()["common.saved"]);

        page.SelectedTemplate = page.Templates.First(t => t.Template.Id == "history.ottoman");
        page.ApplyTemplate();
        page.Name.ShouldBe("Osmanlı Defteri");
        page.ConceptText = page.ConceptText + "\nMy own line.";
        await page.SaveAsync();

        var saved = (await h.Get<IProfileStore>().ListAsync(CancellationToken.None)).Single();
        saved.TemplateId.ShouldBe("history.ottoman");
        saved.ConceptText.ShouldEndWith("My own line.");
        h.Get<AppSession>().Profile!.Id.ShouldBe(saved.Id);
        h.Get<ITemplateSource>().Find("history.ottoman")!.Concept.Tr.ShouldNotContain("My own line.");
    }

    [AvaloniaFact]
    public async Task SampleImagesAreCopiedAndDeletingTheProfileKeepsFiles()
    {
        var confirm = new FakeConfirm();
        using var h = new DesktopHarness(s => s.AddSingleton<IConfirmDialog>(confirm));
        await FirstRunTests.CreateAsync(h);
        var page = h.Get<ProfileViewModel>();
        page.StartNew();
        page.Name = "Lost Engineering";
        page.ConceptText = "Ancient machines.";
        await page.SaveAsync();

        var source = Path.Combine(h.Paths.DataDirectory, "dome.png");
        await File.WriteAllBytesAsync(source, Png, TestContext.Current.CancellationToken);
        await page.AddImageFilesAsync([source, source]);
        page.Images.Count.ShouldBe(1);
        page.Images[0].Image.Width.ShouldBe(2);
        var copied = LibraryPaths.Resolve(h.Get<IAppPaths>().LibraryDirectory, page.Images[0].Image.FileName);
        File.Exists(copied).ShouldBeTrue();

        confirm.Answer = false;
        await page.DeleteAsync();
        (await h.Get<IProfileStore>().ListAsync(CancellationToken.None)).Count.ShouldBe(1);

        confirm.Answer = true;
        await page.DeleteAsync();
        (await h.Get<IProfileStore>().ListAsync(CancellationToken.None)).ShouldBeEmpty();
        File.Exists(copied).ShouldBeTrue();
    }

    [Fact]
    public void LibraryPathsNeverLeaveTheFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "lib");
        Should.Throw<InvalidOperationException>(() => LibraryPaths.Resolve(root, "../x.txt"));
        Should.Throw<InvalidOperationException>(() => LibraryPaths.Resolve(root, Path.GetFullPath("/etc/passwd")));
        LibraryPaths.Resolve(root, "a/b.png").ShouldStartWith(Path.GetFullPath(root));
        LibraryPaths.SafeName("a:b/c?").ShouldBe("a b c");
        LibraryPaths.SafeName("  ").ShouldBe("item");
    }
}
