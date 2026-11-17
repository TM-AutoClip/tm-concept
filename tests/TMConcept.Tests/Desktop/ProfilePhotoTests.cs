using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TMConcept.Core.Paths;
using TMConcept.Core.Profiles;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// 0.1.19 profile page: a new profile takes sample images and a channel photo before its first save (they are copied by Save and dropped by
/// Cancel), and the channel photo is saved, replaced, removed and deleted with the profile.
/// </summary>
public sealed class ProfilePhotoTests
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAACXBIWXMAAAABAAAAAQBPJcTWAAAAEElEQVR4nGP8ywACLGCSAQANEQED1LYyQAAAAABJRU5ErkJggg==");

    private static readonly byte[] OtherPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGNgYGAAAAAEAAH2FzhVAAAAAElFTkSuQmCC");

    [AvaloniaFact]
    public async Task ANewProfileTakesImagesAndAPhotoThatSaveCopiesAndCancelDrops()
    {
        using var h = new DesktopHarness();
        await FirstRunTests.CreateAsync(h);
        var library = h.Get<IAppPaths>().LibraryDirectory;
        var source = Path.Combine(h.Paths.DataDirectory, "dome.png");
        var photo = Path.Combine(h.Paths.DataDirectory, "avatar.png");
        await File.WriteAllBytesAsync(source, Png, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(photo, OtherPng, TestContext.Current.CancellationToken);
        var page = h.Get<ProfileViewModel>();

        // Picked while new, then cancelled: nothing was copied or stored.
        page.StartNew();
        await page.AddImageFilesAsync([source]);
        page.ChooseChannelPhoto(photo);
        page.Images.ShouldHaveSingleItem().Pending.ShouldBeTrue();
        page.ChannelPhotoPath.ShouldBe(photo, "the round preview shows the picked file");
        await page.CancelNewAsync();
        page.Images.ShouldBeEmpty();
        page.ChannelPhotoPath.ShouldBeNull();
        page.IsNew.ShouldBeTrue("with no profile yet, Cancel starts an empty new one");
        (Directory.Exists(library) && Directory.EnumerateFiles(library, "*", SearchOption.AllDirectories).Any()).ShouldBeFalse("nothing was copied");

        // Picked while new, then saved: copied under the saved name and stored with the profile.
        page.Name = "Lost Engineering";
        page.ConceptText = "Ancient machines.";
        await page.AddImageFilesAsync([source, source]);
        page.Images.Count.ShouldBe(1, "the same image twice is one");
        page.Images[0].Caption = "dome from inside";
        page.ChooseChannelPhoto(photo);
        await page.SaveAsync();

        page.Status.ShouldBe(h.Get<Localizer>()["common.saved"]);
        page.IsNew.ShouldBeFalse();
        var saved = (await h.Get<IProfileStore>().ListAsync(CancellationToken.None)).ShouldHaveSingleItem();
        var image = (await h.Get<IProfileStore>().ImagesAsync(saved.Id, CancellationToken.None)).ShouldHaveSingleItem();
        image.Caption.ShouldBe("dome from inside");
        (image.Width, image.Height).ShouldBe((2, 2));
        image.FileName.ShouldStartWith("Lost Engineering/images/");
        File.ReadAllBytes(LibraryPaths.Resolve(library, image.FileName)).ShouldBe(Png);
        page.Images.ShouldHaveSingleItem().Pending.ShouldBeFalse();
        page.Images[0].FullPath.ShouldBe(LibraryPaths.Resolve(library, image.FileName));
        saved.ChannelPhoto.ShouldNotBeNull().ShouldStartWith("Lost Engineering/channel/");
        File.ReadAllBytes(LibraryPaths.Resolve(library, saved.ChannelPhoto)).ShouldBe(OtherPng);
        page.ChannelPhotoPath.ShouldBe(LibraryPaths.Resolve(library, saved.ChannelPhoto));
    }

    [AvaloniaFact]
    public async Task TheChannelPhotoIsReplacedAndRemovedBySaveAndDeletedWithTheProfile()
    {
        var confirm = new FakeConfirm();
        using var h = new DesktopHarness(s => s.AddSingleton<IConfirmDialog>(confirm));
        await FirstRunTests.CreateAsync(h);
        var library = h.Get<IAppPaths>().LibraryDirectory;
        var first = Path.Combine(h.Paths.DataDirectory, "first.png");
        var second = Path.Combine(h.Paths.DataDirectory, "second.png");
        var broken = Path.Combine(h.Paths.DataDirectory, "broken.png");
        await File.WriteAllBytesAsync(first, Png, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(second, OtherPng, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(broken, [1, 2, 3], TestContext.Current.CancellationToken);
        var page = h.Get<ProfileViewModel>();
        page.StartNew();
        page.Name = "Byzantium";
        page.ConceptText = "History.";
        await page.SaveAsync();
        var id = h.Get<AppSession>().Profile!.Id;

        page.ChooseChannelPhoto(first);
        await page.SaveAsync();
        var firstCopy = LibraryPaths.Resolve(library, (await h.Get<IProfileStore>().GetAsync(id, CancellationToken.None))!.ChannelPhoto!);
        File.Exists(firstCopy).ShouldBeTrue();

        // Not an image: said, the stored photo stays.
        page.ChooseChannelPhoto(broken);
        page.Status.ShouldContain("broken.png");
        page.ChannelPhotoPath.ShouldBe(firstCopy);

        // Replaced: the new copy is stored, the old one is gone.
        page.ChooseChannelPhoto(second);
        (await h.Get<IProfileStore>().GetAsync(id, CancellationToken.None))!.ChannelPhoto.ShouldNotBeNull("not saved yet");
        await page.SaveAsync();
        var secondCopy = LibraryPaths.Resolve(library, (await h.Get<IProfileStore>().GetAsync(id, CancellationToken.None))!.ChannelPhoto!);
        secondCopy.ShouldNotBe(firstCopy);
        File.Exists(firstCopy).ShouldBeFalse();
        File.ReadAllBytes(secondCopy).ShouldBe(OtherPng);

        // Removed: Save clears it and deletes the copy.
        page.RemoveChannelPhoto();
        page.HasChannelPhoto.ShouldBeFalse();
        await page.SaveAsync();
        (await h.Get<IProfileStore>().GetAsync(id, CancellationToken.None))!.ChannelPhoto.ShouldBeNull();
        File.Exists(secondCopy).ShouldBeFalse();

        // Deleted with the profile.
        page.ChooseChannelPhoto(first);
        await page.SaveAsync();
        var lastCopy = LibraryPaths.Resolve(library, (await h.Get<IProfileStore>().GetAsync(id, CancellationToken.None))!.ChannelPhoto!);
        File.Exists(lastCopy).ShouldBeTrue();
        await page.DeleteAsync();
        (await h.Get<IProfileStore>().ListAsync(CancellationToken.None)).ShouldBeEmpty();
        File.Exists(lastCopy).ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task TheAddImagesButtonWorksOnANewProfileAndThePhotoHasARoundPreview()
    {
        using var h = new DesktopHarness();
        await FirstRunTests.CreateAsync(h);
        var page = h.Get<ProfileViewModel>();
        page.StartNew();
        var view = new TMConcept.Desktop.Views.ProfileView { DataContext = page };
        var window = new Avalonia.Controls.Window { Content = view, Width = 1100, Height = 900 };
        h.Get<LanguageResources>().Attach(window.Resources.MergedDictionaries);
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var controls = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view).OfType<Avalonia.Controls.Control>().ToList();
        controls.OfType<Avalonia.Controls.Button>().Single(b => b.Name == "AddImagesButton").IsEffectivelyEnabled.ShouldBeTrue("0.1.18 disabled it until the first save");
        controls.OfType<Avalonia.Controls.Button>().Single(b => b.Name == "CancelNewButton").IsVisible.ShouldBeTrue();
        var round = controls.OfType<Avalonia.Controls.Border>().Single(b => b.Name == "ChannelPhoto");
        round.CornerRadius.TopLeft.ShouldBe(round.Width / 2);
        window.Close();
    }
}
