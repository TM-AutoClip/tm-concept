using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TMConcept.Core.Paths;
using TMConcept.Core.Profiles;
using TMConcept.Core.Videos;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Os;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Tests.Desktop;

public sealed class ProducedPageTests
{
    [AvaloniaFact]
    public async Task DetailsShowTheVoiceNotesAndDeletingCanMoveTheFilesToTheRecycleBin()
    {
        var confirm = new FakeConfirm { Checked = true };
        var bin = new FakeBin();
        using var h = new DesktopHarness(s =>
        {
            s.AddSingleton<IConfirmDialog>(confirm);
            s.AddSingleton<IRecycleBin>(bin);
        });
        await FirstRunTests.CreateAsync(h);
        var profile = new Profile { Name = "Byzantium", ContentLanguage = ContentLanguages.Turkish, ConceptText = "x" };
        await h.Get<IProfileStore>().SaveAsync(profile, CancellationToken.None);
        await h.Get<AppSession>().SelectProfileAsync(profile, CancellationToken.None);
        var folder = Path.Combine(h.Get<IAppPaths>().LibraryDirectory, "Byzantium", "2026-11-05-1-greek-fire");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "video.mp4"), new byte[16]);
        var video = new VideoRecord
        {
            ProfileId = profile.Id,
            Title = "Greek fire",
            Folder = LibraryPaths.Relative(h.Get<IAppPaths>().LibraryDirectory, folder),
            FileName = "video.mp4",
            Engine = "HyperFrames",
            Warnings = VideoWarnings.NonCommercialVoice,
            TotalUsd = 0.12m,
        };
        await h.Get<IVideoStore>().AddAsync(video, [new VideoCost { VideoId = video.Id, Step = "video-code", Usd = 0.12m, Exact = true }], CancellationToken.None);
        var page = h.Get<ProducedViewModel>();
        await page.ActivateAsync();

        page.Selected = page.Items.Single();
        for (var i = 0; i < 100 && page.Details.Length == 0; i++)
        {
            await Task.Delay(20);
        }

        page.Details.ShouldContain("CC BY-NC-SA");
        page.Items.Single().Badges.ShouldNotBeEmpty();
        await page.DeleteAsync();

        bin.Moved.Single().ShouldBe(folder);
        page.Items.ShouldBeEmpty();
        (await h.Get<IVideoStore>().ListAsync(profile.Id, CancellationToken.None)).ShouldBeEmpty();
    }

    private sealed class FakeBin : IRecycleBin
    {
        public List<string> Moved { get; } = [];

        public Task MoveAsync(string folder, CancellationToken cancellationToken)
        {
            Moved.Add(folder);
            return Task.CompletedTask;
        }
    }
}
