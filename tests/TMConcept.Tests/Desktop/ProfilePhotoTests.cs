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
/// 0.1.19 profile page: a new profile takes a channel photo before its first save (it is copied by Save and dropped by Cancel), and the
/// channel photo is saved, replaced, removed and deleted with the profile.
/// </summary>
public sealed class ProfilePhotoTests
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAACXBIWXMAAAABAAAAAQBPJcTWAAAAEElEQVR4nGP8ywACLGCSAQANEQED1LYyQAAAAABJRU5ErkJggg==");

    private static readonly byte[] OtherPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGNgYGAAAAAEAAH2FzhVAAAAAElFTkSuQmCC");

    [AvaloniaFact]
    public async Task ANewProfileTakesAPhotoThatSaveCopiesAndCancelDrops()
    {
        using var h = new DesktopHarness();
        await FirstRunTests.CreateAsync(h);
        var library = h.Get<IAppPaths>().LibraryDirectory;
        var photo = Path.Combine(h.Paths.DataDirectory, "avatar.png");
        await File.WriteAllBytesAsync(photo, OtherPng, TestContext.Current.CancellationToken);
        var page = h.Get<ProfileViewModel>();

        // Picked while new, then cancelled: nothing was copied or stored.
        page.StartNew();
        page.ChooseChannelPhoto(photo);
        page.ChannelPhotoPath.ShouldBe(photo, "the round preview shows the picked file");
        await page.CancelNewAsync();
        page.ChannelPhotoPath.ShouldBeNull();
        page.IsNew.ShouldBeTrue("with no profile yet, Cancel starts an empty new one");
        (Directory.Exists(library) && Directory.EnumerateFiles(library, "*", SearchOption.AllDirectories).Any()).ShouldBeFalse("nothing was copied");

        // Picked while new, then saved: copied under the saved name and stored with the profile.
        page.Name = "Lost Engineering";
        page.ConceptText = "Ancient machines.";
        page.ChooseChannelPhoto(photo);
        await page.SaveAsync();

        page.Status.ShouldBe(h.Get<Localizer>()["common.saved"]);
        page.IsNew.ShouldBeFalse();
        var saved = (await h.Get<IProfileStore>().ListAsync(CancellationToken.None)).ShouldHaveSingleItem();
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
    public async Task AFailedSaveRemovesTheFreshCopyAndAReplacementLaterDeletesTheRightOldPhoto()
    {
        FlakyProfileStore? flaky = null;
        using var h = new DesktopHarness(s =>
        {
            s.AddSingleton<TMConcept.Storage.Stores.ProfileStore>();
            s.AddSingleton<IProfileStore>(sp => flaky = new FlakyProfileStore(sp.GetRequiredService<TMConcept.Storage.Stores.ProfileStore>()));
        });
        await FirstRunTests.CreateAsync(h);
        var library = h.Get<IAppPaths>().LibraryDirectory;
        var first = Path.Combine(h.Paths.DataDirectory, "first.png");
        var second = Path.Combine(h.Paths.DataDirectory, "second.png");
        await File.WriteAllBytesAsync(first, Png, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(second, OtherPng, TestContext.Current.CancellationToken);
        var page = h.Get<ProfileViewModel>();
        page.StartNew();
        page.Name = "Byzantium";
        page.ConceptText = "History.";
        page.ChooseChannelPhoto(first);
        await page.SaveAsync();
        var id = h.Get<AppSession>().Profile!.Id;
        var firstCopy = LibraryPaths.Resolve(library, (await h.Get<IProfileStore>().GetAsync(id, CancellationToken.None))!.ChannelPhoto!);

        // The database refuses the save: the new copy is gone, the row still points at the first photo.
        page.ChooseChannelPhoto(second);
        flaky.ShouldNotBeNull().FailNextSave = true;
        await page.SaveAsync();
        page.Status.ShouldContain("database is locked");
        (await h.Get<IProfileStore>().GetAsync(id, CancellationToken.None))!.ChannelPhoto.ShouldNotBeNull().ShouldEndWith(Path.GetFileName(firstCopy));
        Directory.GetFiles(Path.GetDirectoryName(firstCopy)!).Select(f => Path.GetFileName(f)).ShouldBe([Path.GetFileName(firstCopy)], "the fresh copy of the failed save is removed");

        // Saved again: the first photo (the stored one) is the one deleted, nothing is orphaned.
        await page.SaveAsync();
        var secondCopy = LibraryPaths.Resolve(library, (await h.Get<IProfileStore>().GetAsync(id, CancellationToken.None))!.ChannelPhoto!);
        File.ReadAllBytes(secondCopy).ShouldBe(OtherPng);
        Directory.GetFiles(Path.GetDirectoryName(firstCopy)!).Select(f => Path.GetFileName(f)).ShouldBe([Path.GetFileName(secondCopy)]);
    }

    [AvaloniaFact]
    public async Task ANewProfileWhosePickedPhotoIsGoneIsNotSavedAtAllAndCancelDropsIt()
    {
        using var h = new DesktopHarness();
        await FirstRunTests.CreateAsync(h);
        var library = h.Get<IAppPaths>().LibraryDirectory;
        var gone = Path.Combine(h.Paths.DataDirectory, "gone.png");
        await File.WriteAllBytesAsync(gone, OtherPng, TestContext.Current.CancellationToken);
        var page = h.Get<ProfileViewModel>();
        page.StartNew();
        page.Name = "Lost Engineering";
        page.ConceptText = "Ancient machines.";
        page.ChooseChannelPhoto(gone);
        File.Delete(gone);

        await page.SaveAsync();

        page.Status.ShouldContain("gone.png");
        page.IsNew.ShouldBeTrue("nothing was saved, so the page still offers Cancel");
        (await h.Get<IProfileStore>().ListAsync(CancellationToken.None)).ShouldBeEmpty();
        (Directory.Exists(library) && Directory.EnumerateFiles(library, "*", SearchOption.AllDirectories).Any()).ShouldBeFalse("nothing was copied");
        await page.CancelNewAsync();
        (await h.Get<IProfileStore>().ListAsync(CancellationToken.None)).ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task TwoProfilesOfTheSameNameWithTheSamePhotoKeepTheirOwnCopies()
    {
        var confirm = new FakeConfirm();
        using var h = new DesktopHarness(s => s.AddSingleton<IConfirmDialog>(confirm));
        await FirstRunTests.CreateAsync(h);
        var library = h.Get<IAppPaths>().LibraryDirectory;
        var logo = Path.Combine(h.Paths.DataDirectory, "logo.png");
        await File.WriteAllBytesAsync(logo, Png, TestContext.Current.CancellationToken);
        var page = h.Get<ProfileViewModel>();
        foreach (var _ in new[] { 1, 2 })
        {
            page.StartNew();
            page.Name = "Tarih";
            page.ConceptText = "History.";
            page.ChooseChannelPhoto(logo);
            await page.SaveAsync();
        }

        var photos = (await h.Get<IProfileStore>().ListAsync(CancellationToken.None)).Select(p => p.ChannelPhoto!).ToList();
        photos.Count.ShouldBe(2);
        photos.Distinct().Count().ShouldBe(2, "the copy's name carries the profile's id");

        // The second one (selected) is deleted; the first one's photo stays.
        await page.DeleteAsync();
        var left = (await h.Get<IProfileStore>().ListAsync(CancellationToken.None)).ShouldHaveSingleItem();
        File.ReadAllBytes(LibraryPaths.Resolve(library, left.ChannelPhoto!)).ShouldBe(Png);
    }

    [AvaloniaFact]
    public async Task ANewProfileOffersCancelAndThePhotoHasARoundPreview()
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
        controls.OfType<Avalonia.Controls.Button>().Single(b => b.Name == "CancelNewButton").IsVisible.ShouldBeTrue();
        var round = controls.OfType<Avalonia.Controls.Border>().Single(b => b.Name == "ChannelPhoto");
        round.CornerRadius.TopLeft.ShouldBe(round.Width / 2);
        window.Close();
    }
}
