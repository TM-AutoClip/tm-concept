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
    public async Task TheChannelContentBoxSaysItDrivesEveryConceptAndWarnsWhenItIsVeryShort()
    {
        using var h = new DesktopHarness();
        await FirstRunTests.CreateAsync(h);
        var page = h.Get<ProfileViewModel>();
        page.StartNew();
        var view = new TMConcept.Desktop.Views.ProfileView { DataContext = page };
        var window = new Avalonia.Controls.Window { Content = view, Width = 1100, Height = 900 };
        h.Get<TMConcept.Desktop.Localization.LanguageResources>().Attach(window.Resources.MergedDictionaries);
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var text = h.Get<TMConcept.Desktop.Localization.Localizer>();

        var box = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view).OfType<Avalonia.Controls.TextBox>().Single(b => b.Name == "ConceptBox");
        box.PlaceholderText.ShouldBe(text["profile.concept.placeholder"]);
        var tips = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view).OfType<TMConcept.Desktop.Views.HelpTip>().Select(t => t.Text).ToList();
        tips.ShouldContain(text["profile.concept.hint"]);
        tips.ShouldContain(text["profile.name.hint"], "the name's \"?\" says it is not sent to the model");
        var warning = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view).OfType<Avalonia.Controls.TextBlock>().Single(b => b.Name == "ConceptShort");

        page.ConceptText = string.Empty;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        warning.IsVisible.ShouldBeFalse("nothing written yet: the save check says so");
        page.ConceptText = "History channel.";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        warning.IsVisible.ShouldBeTrue();
        page.ConceptText = new string('x', ProfileLimits.ConceptTextAdvised);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        warning.IsVisible.ShouldBeFalse();
        window.Close();
    }

    [AvaloniaFact]
    public async Task TheChannelPhotoIsCopiedAndDeletingTheProfileDeletesIt()
    {
        var confirm = new FakeConfirm();
        using var h = new DesktopHarness(s => s.AddSingleton<IConfirmDialog>(confirm));
        await FirstRunTests.CreateAsync(h);
        var page = h.Get<ProfileViewModel>();
        page.StartNew();
        page.Name = "Lost Engineering";
        page.ConceptText = "Ancient machines.";
        var source = Path.Combine(h.Paths.DataDirectory, "avatar.png");
        await File.WriteAllBytesAsync(source, Png, TestContext.Current.CancellationToken);
        page.ChooseChannelPhoto(source);
        await page.SaveAsync();

        var stored = (await h.Get<IProfileStore>().ListAsync(CancellationToken.None)).ShouldHaveSingleItem().ChannelPhoto.ShouldNotBeNull();
        var copied = LibraryPaths.Resolve(h.Get<IAppPaths>().LibraryDirectory, stored);
        File.Exists(copied).ShouldBeTrue();

        confirm.Answer = false;
        await page.DeleteAsync();
        (await h.Get<IProfileStore>().ListAsync(CancellationToken.None)).Count.ShouldBe(1);

        confirm.Answer = true;
        await page.DeleteAsync();
        (await h.Get<IProfileStore>().ListAsync(CancellationToken.None)).ShouldBeEmpty();
        File.Exists(copied).ShouldBeFalse("0.1.28: deleting a profile deletes what it generated");
        Directory.Exists(Path.Combine(h.Get<IAppPaths>().LibraryDirectory, "Lost Engineering")).ShouldBeFalse("its emptied folder goes too");
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
