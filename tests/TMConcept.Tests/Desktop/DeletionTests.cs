using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TMConcept.Core.Components;
using TMConcept.Core.Concepts;
using TMConcept.Core.Deletion;
using TMConcept.Core.DiskSpace;
using TMConcept.Core.Documents;
using TMConcept.Core.Paths;
using TMConcept.Core.Profiles;
using TMConcept.Core.Speech;
using TMConcept.Core.Videos;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.DiskSpace;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// 0.1.28 (the developer: deleting something deletes everything it generated; nothing leaks; big deletes free space for real; models only
/// when chosen): a video, trashed concepts and a profile take their files with them for good, files first and the record
/// after (a file in use keeps the record and is reported); a file another record still uses stays; the storage page finds library files
/// without a record and takes them only when ticked; the clean-ups never take a model. Everything runs in the test's own temporary folders
/// with the app's real permanent delete (it refuses any path outside them).
/// </summary>
public sealed class DeletionTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [AvaloniaFact]
    public async Task AVideoDeleteTakesItsVersionFolderAndTheConceptFolderWithItsLastVersion()
    {
        using var h = await HarnessAsync();
        var profile = await ProfileAsync(h, "Byzantium");
        var v1 = await VideoAsync(h, profile, "Byzantium/2026-11-05-1-greek-fire/v1");
        var v2 = await VideoAsync(h, profile, "Byzantium/2026-11-05-1-greek-fire/v2");
        var deletion = h.Get<VideoDeletion>();

        (await deletion.PlanAsync([v1.Id], None)).ShouldBe(new DeletionPlan(0, 1, 0, 16 + 4));
        var first = await deletion.DeleteAsync([v1.Id], None);

        first.Complete.ShouldBeTrue();
        (first.Deleted, first.Freed).ShouldBe((1, 20L));
        Directory.Exists(Full(h, v1.Folder)).ShouldBeFalse();
        Directory.Exists(Full(h, v2.Folder)).ShouldBeTrue("the other version stays");
        (await h.Get<IVideoStore>().GetAsync(v1.Id, None)).ShouldBeNull();

        await deletion.DeleteAsync([v2.Id], None);

        Directory.Exists(Path.Combine(h.Paths.LibraryDirectory, "Byzantium")).ShouldBeFalse("the emptied concept and profile folders go too");
        Directory.Exists(h.Paths.LibraryDirectory).ShouldBeTrue("never the library folder itself");
    }

    [AvaloniaFact]
    public async Task AFolderInUseKeepsTheVideoRecordAndASecondTryFinishesIt()
    {
        var discard = new RecordingDiscard();
        using var h = await HarnessAsync(discard);
        var profile = await ProfileAsync(h, "Byzantium");
        var video = await VideoAsync(h, profile, "Byzantium/greek-fire/v1");
        discard.FailWhen = p => p == Full(h, video.Folder);

        var result = await h.Get<VideoDeletion>().DeleteAsync([video.Id], None);

        result.Complete.ShouldBeFalse();
        result.Failed.ShouldHaveSingleItem().Reason.ShouldContain("being used by another process");
        (await h.Get<IVideoStore>().GetAsync(video.Id, None)).ShouldNotBeNull("a record never goes before its own files");
        Directory.Exists(Full(h, video.Folder)).ShouldBeTrue();

        discard.FailWhen = _ => false;
        (await h.Get<VideoDeletion>().DeleteAsync([video.Id], None)).Complete.ShouldBeTrue();
        (await h.Get<IVideoStore>().GetAsync(video.Id, None)).ShouldBeNull();
    }

    [AvaloniaFact]
    public async Task DeletingTrashedConceptsTakesTheirVideosAndSceneImagesAndLeavesTheOthers()
    {
        using var h = await HarnessAsync();
        var profile = await ProfileAsync(h, "Byzantium");
        var trashed = await ConceptAsync(h, profile, "Greek fire", ConceptStatus.Trashed);
        var kept = await ConceptAsync(h, profile, "Walls", ConceptStatus.Produced);
        var gone = await VideoAsync(h, profile, "Byzantium/greek-fire/v1", trashed.Id);
        var stays = await VideoAsync(h, profile, "Byzantium/walls/v1", kept.Id);
        var images = VideoPipeline.SceneImageFolder(h.Paths.WorkDirectory, trashed.Id);
        Put(images, "1.png", 30);
        var keptImages = VideoPipeline.SceneImageFolder(h.Paths.WorkDirectory, kept.Id);
        Put(keptImages, "1.png", 30);
        var deletion = h.Get<ConceptDeletion>();

        (await deletion.PlanAsync([trashed.Id, kept.Id], None)).ShouldBe(new DeletionPlan(1, 1, 0, 20 + 30), "only a concept in the trash counts");
        var result = await deletion.DeleteAsync([trashed.Id, kept.Id], None);

        (result.Deleted, result.Complete).ShouldBe((1, true));
        (await h.Get<IConceptStore>().GetAsync(trashed.Id, None)).ShouldBeNull();
        (await h.Get<IVideoStore>().GetAsync(gone.Id, None)).ShouldBeNull("its videos go with it (until 0.1.28 they stayed without a concept)");
        Directory.Exists(Full(h, gone.Folder)).ShouldBeFalse();
        Directory.Exists(images).ShouldBeFalse();
        (await h.Get<IConceptStore>().GetAsync(kept.Id, None)).ShouldNotBeNull("not in the trash: never touched");
        Directory.Exists(Full(h, stays.Folder)).ShouldBeTrue();
        Directory.Exists(keptImages).ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task DeletingAProfileTakesEverythingItMadeButNothingASameNamedProfileUses()
    {
        using var h = await HarnessAsync();
        var mine = await ProfileAsync(h, "History");
        var other = await ProfileAsync(h, "History");
        var profiles = h.Get<IProfileStore>();
        var photo = mine.Id.ToString("N") + "-0123456789.png";
        Put(Path.Combine(h.Paths.LibraryDirectory, "History", "channel"), photo, 12);
        mine.ChannelPhoto = "History/channel/" + photo;
        await profiles.SaveAsync(mine, None);
        var concept = await ConceptAsync(h, mine, "Greek fire", ConceptStatus.Produced);
        var video = await VideoAsync(h, mine, "History/greek-fire/v1", concept.Id);
        var theirs = await VideoAsync(h, other, "History/walls/v1");
        var images = VideoPipeline.SceneImageFolder(h.Paths.WorkDirectory, concept.Id);
        Put(images, "1.png", 30);
        var documents = h.Get<DocumentLibrary>();
        var source = Path.Combine(h.Paths.DataDirectory, "walls.txt");
        File.WriteAllText(source, "The walls held.");
        await documents.AddFileAsync(mine.Id, source, null, None);
        var deletion = h.Get<ProfileDeletion>();

        var plan = await deletion.PlanAsync(mine.Id, None);
        (plan.Concepts, plan.Videos, plan.Documents).ShouldBe((1, 1, 1));
        plan.Bytes.ShouldBe(20 + 12 + 30 + 15, "the same-named profile's video is not counted: it stays");
        var result = await deletion.DeleteAsync(mine.Id, None);

        (result.Deleted, result.Complete).ShouldBe((1, true));
        (await profiles.GetAsync(mine.Id, None)).ShouldBeNull();
        Directory.Exists(Full(h, video.Folder)).ShouldBeFalse();
        File.Exists(Path.Combine(h.Paths.LibraryDirectory, "History", "channel", photo)).ShouldBeFalse();
        Directory.Exists(images).ShouldBeFalse();
        Directory.Exists(Path.Combine(documents.Folder, mine.Id.ToString("N"))).ShouldBeFalse();
        Directory.Exists(Full(h, theirs.Folder)).ShouldBeTrue("the same-named profile's video is its own");
        (await profiles.GetAsync(other.Id, None)).ShouldNotBeNull();
    }

    [AvaloniaFact]
    public async Task AProfileWhoseFileIsInUseStaysAndTheFilesAreListed()
    {
        var discard = new RecordingDiscard();
        var confirm = new FakeConfirm();
        using var h = await HarnessAsync(discard, confirm);
        var profile = await ProfileAsync(h, "Byzantium");
        var video = await VideoAsync(h, profile, "Byzantium/greek-fire/v1");
        await h.Get<AppSession>().SelectProfileAsync(profile, None);
        var page = h.Get<ProfileViewModel>();
        await page.ActivateAsync();
        discard.FailWhen = p => p == Full(h, video.Folder);
        var text = h.Get<Localizer>();

        await page.DeleteAsync();

        confirm.Messages.ShouldHaveSingleItem().ShouldBe(text.Format("profile.delete.message", "Byzantium", 0, 1, 0, StorageSettingsViewModel.Size(20, text.Culture)));
        (await h.Get<IProfileStore>().GetAsync(profile.Id, None)).ShouldNotBeNull("nothing becomes an orphan");
        page.Status.ShouldBe(text.Format("profile.delete.in_use", "• v1 (The process cannot access the file because it is being used by another process.)"));

        discard.FailWhen = _ => false;
        await page.DeleteAsync();
        (await h.Get<IProfileStore>().GetAsync(profile.Id, None)).ShouldBeNull("a second try finishes it");
        Directory.Exists(Full(h, video.Folder)).ShouldBeFalse();
    }

    [Fact]
    public void TheOrphanScanListsOnlyTheAppsOwnFilesThatNoRecordPointsAt()
    {
        var library = TestFolders.New("library-");
        try
        {
            var p = Path.Combine(library, "History");
            Put(Path.Combine(p, "fire", "v1"), VideoFolders.VideoFile, 5);
            Put(Path.Combine(p, "fire", "v1", VideoFolders.Source), "input.json", 5);
            Put(Path.Combine(p, "fire", "v2"), VideoFolders.VideoFile, 5);
            Put(Path.Combine(p, "walls", "v1"), VideoFolders.VideoFile, 5);
            Put(Path.Combine(p, "walls", "v1"), "my notes.docx", 5);
            Put(Path.Combine(p, "my stuff"), "holiday.mp4", 5);
            Put(p, "loose.txt", 5);
            Put(Path.Combine(p, "images"), "0123456789-dome.png", 5);
            Put(Path.Combine(p, "images"), "my photo.png", 5);
            Put(Path.Combine(p, "channel"), new string('a', 32) + "-0123456789.png", 5);
            Put(Path.Combine(p, "channel"), new string('b', 32) + "-0123456789.png", 5);

            var (folders, files) = LibraryOrphanSource.Scan(library, ["History/fire/v2"], ["History/channel/" + new string('b', 32) + "-0123456789.png", null]);

            folders.ShouldBe([Path.Combine(p, "fire", "v1")]);
            files.ShouldBe([Path.Combine(p, "channel", new string('a', 32) + "-0123456789.png"), Path.Combine(p, "images", "0123456789-dome.png")],
                ignoreOrder: true, "a sample image copy is used by nothing; a photo no profile points at is a leftover");
        }
        finally
        {
            TestFolders.Delete(library);
        }
    }

    [AvaloniaFact]
    public async Task OrphanRowsGoOnlyWhenTickedAndAFolderThatGotARecordMeanwhileStays()
    {
        using var h = await HarnessAsync();
        var profile = await ProfileAsync(h, "Byzantium");
        Put(Path.Combine(h.Paths.LibraryDirectory, "Byzantium", "fire", "v1"), VideoFolders.VideoFile, 7);
        Put(Path.Combine(h.Paths.LibraryDirectory, "Byzantium", "walls", "v1"), VideoFolders.VideoFile, 9);
        var cleaner = h.Get<IDiskCleaner>();

        var report = await cleaner.ReportAsync(None);

        var row = report.Items.Single(i => i.Key == LibraryOrphanSource.VideosKey);
        (row.Area, row.Bytes, row.Paths.Count).ShouldBe((DiskArea.Library, 16L, 2));
        DiskSelection.Unused(report.Items).ShouldNotContain(row, "a delete before 0.1.28 kept these files on purpose");
        DiskSelection.Full(report.Items).ShouldNotContain(row);
        report.Totals.ShouldContain(t => t.Area == DiskArea.Library && t.Bytes == 16);

        // A video finished after the page was listed: its folder has a record now and stays.
        await VideoAsync(h, profile, "Byzantium/walls/v1", write: false);
        (await cleaner.RemoveAsync([row], None)).Removed.ShouldBe([row]);

        Directory.Exists(Path.Combine(h.Paths.LibraryDirectory, "Byzantium", "fire")).ShouldBeFalse("the orphan and its emptied concept folder go");
        File.Exists(Path.Combine(h.Paths.LibraryDirectory, "Byzantium", "walls", "v1", VideoFolders.VideoFile)).ShouldBeTrue();
        h.Get<AppSession>().WorkRunning = true;
        Put(Path.Combine(h.Paths.LibraryDirectory, "Byzantium", "new", "v1"), VideoFolders.VideoFile, 7);
        (await cleaner.ReportAsync(None)).Items.ShouldNotContain(i => i.Key == LibraryOrphanSource.VideosKey,
            "a running video writes its folder before its record");
    }

    [AvaloniaFact]
    public async Task SceneImagesOfAConceptThatIsGoneAreALeftoverAtOnce()
    {
        using var h = await HarnessAsync();
        var profile = await ProfileAsync(h, "Byzantium");
        var concept = await ConceptAsync(h, profile, "Walls", ConceptStatus.Approved);
        Put(VideoPipeline.SceneImageFolder(h.Paths.WorkDirectory, concept.Id), "1.png", 30);
        var gone = VideoPipeline.SceneImageFolder(h.Paths.WorkDirectory, Guid.NewGuid());
        Put(gone, "1.png", 40);

        var report = await h.Get<IDiskCleaner>().ReportAsync(None);

        report.Items.Single(i => i.Key == "work:scene-images:orphan").Paths.ShouldBe([gone]);
        report.Items.Single(i => i.Key == "work:scene-images:recent").Bytes.ShouldBe(30);
        DiskSelection.Unused(report.Items).ShouldContain(i => i.Key == "work:scene-images:orphan");
    }

    [Theory]
    [InlineData("ocr.pp-ocr-latin", DiskArea.Models)]
    [InlineData("ocr.directml", DiskArea.Models)]
    [InlineData("voice.kokoro", DiskArea.Models)]
    [InlineData("ffmpeg", DiskArea.Tools)]
    public void TheTextRecognitionIsKeptLikeAModel(string code, DiskArea area)
    {
        ComponentDiskItem.AreaOf(code).ShouldBe(area);
        PageTextEngines.LocalOcrComponent.ShouldStartWith(ComponentCodes.OcrPrefix);
    }

    [AvaloniaFact]
    public async Task DeletingAVoiceSampleDeletesTheNarrationSpokenWithItAndNothingElse()
    {
        using var h = await HarnessAsync();
        var sample = new VoiceSample { Name = "Me", Wav = [1, 2, 3], Sha256 = "x" };
        await h.Get<IVoiceSampleStore>().AddAsync(sample, None);
        var cache = Path.Combine(h.Paths.WorkDirectory, "speech-cache");
        var mine = SpeechService.VoiceTag(VoiceSampleLibrary.VoiceId(sample.Id));
        Put(cache, mine + "-aaaa.wav", 5);
        Put(cache, mine + "-bbbb.wav", 5);
        Put(cache, SpeechService.VoiceTag("kokoro:af_heart") + "-cccc.wav", 5);
        Put(Path.Combine(h.Paths.WorkDirectory, "listen"), "voice-sample.wav", 5);

        await h.Get<VoiceSampleLibrary>().DeleteAsync(sample.Id, None);

        Directory.GetFiles(cache).Select(Path.GetFileName).ShouldBe([SpeechService.VoiceTag("kokoro:af_heart") + "-cccc.wav"]);
        File.Exists(Path.Combine(h.Paths.WorkDirectory, "listen", "voice-sample.wav")).ShouldBeFalse();
        (await h.Get<IVoiceSampleStore>().GetAsync(sample.Id, None)).ShouldBeNull();
    }

    [AvaloniaFact]
    public async Task OldWorkFilesGoAtTheEndOfTheQueueAndRecentOnesStay()
    {
        using var h = await HarnessAsync();
        var cache = Path.Combine(h.Paths.WorkDirectory, "speech-cache");
        var voices = Path.Combine(h.Paths.WorkDirectory, "voices");
        Put(cache, "old.wav", 5);
        File.SetLastWriteTimeUtc(Path.Combine(cache, "old.wav"), DateTime.UtcNow.AddDays(-8));
        Put(cache, "recent.wav", 5);
        Put(voices, "import-1.wav", 5);
        File.SetLastWriteTimeUtc(Path.Combine(voices, "import-1.wav"), DateTime.UtcNow.AddDays(-2));
        Put(voices, "reference-2.wav", 5);
        Put(Path.Combine(h.Paths.WorkDirectory, "listen"), "sample.wav", 5);

        h.Get<VideoPipeline>().CleanOld();

        Directory.GetFiles(cache).Select(Path.GetFileName).ShouldBe(["recent.wav"]);
        Directory.GetFiles(voices).Select(Path.GetFileName).ShouldBe(["reference-2.wav"], "one a request may be using right now stays");
        File.Exists(Path.Combine(h.Paths.WorkDirectory, "listen", "sample.wav")).ShouldBeTrue("the queue end never takes what a page may be playing");

        h.Get<VideoPipeline>().CleanWork();
        Directory.Exists(Path.Combine(h.Paths.WorkDirectory, "listen")).ShouldBeFalse("at start nothing plays");
    }

    [Fact]
    public async Task TheAppsDeleteNeverLeavesTheTestsOwnFolders()
    {
        using var h = new DesktopHarness();
        FolderDiscardBase.Inside(h.Paths.DataDirectory, TestFolders.Root).ShouldBeTrue();
        FolderDiscardBase.Inside(h.Paths.LibraryDirectory, TestFolders.Root).ShouldBeTrue();
        var discard = h.Get<IFolderDiscard>();
        discard.ShouldBeOfType<PermanentDiscard>();
        await Should.ThrowAsync<InvalidOperationException>(() => discard.DiscardAsync(h.Paths.LibraryDirectory, None));
        await Should.ThrowAsync<InvalidOperationException>(() => discard.DiscardAsync(TestFolders.Root, None));
        discard.Owns(Path.Combine(h.Paths.LibraryDirectory, "x")).ShouldBeTrue();
        discard.Owns(Path.Combine(TestFolders.Root, "x")).ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task TwoRecordsOnOneFolderNeverDeleteEachOthersFiles()
    {
        using var h = await HarnessAsync();
        var profile = await ProfileAsync(h, "Byzantium");
        var first = await VideoAsync(h, profile, "Byzantium/greek-fire/v1");
        var second = await VideoAsync(h, profile, "Byzantium/greek-fire/v1", write: false);
        var deletion = h.Get<VideoDeletion>();
        var file = Path.Combine(Full(h, first.Folder), VideoFolders.VideoFile);

        (await deletion.PlanAsync([first.Id], None)).Bytes.ShouldBe(0, "the files are the other record's too");
        (await deletion.DeleteAsync([first.Id], None)).Complete.ShouldBeTrue();

        (await h.Get<IVideoStore>().GetAsync(first.Id, None)).ShouldBeNull("only the record goes");
        File.Exists(file).ShouldBeTrue("the second record still uses the same video.mp4");
        (await h.Get<IVideoStore>().GetAsync(second.Id, None)).ShouldNotBeNull();

        (await deletion.DeleteAsync([second.Id], None)).Complete.ShouldBeTrue();
        Directory.Exists(Full(h, second.Folder)).ShouldBeFalse("the last record takes the folder");
    }

    [AvaloniaFact]
    public async Task AProfileThatStaysKeepsItsDocumentCopies()
    {
        var discard = new RecordingDiscard();
        using var h = await HarnessAsync(discard);
        var profile = await ProfileAsync(h, "Byzantium");
        var video = await VideoAsync(h, profile, "Byzantium/greek-fire/v1");
        var documents = h.Get<DocumentLibrary>();
        var source = Path.Combine(h.Paths.DataDirectory, "walls.txt");
        File.WriteAllText(source, "The walls held.");
        await documents.AddFileAsync(profile.Id, source, null, None);
        var copies = Path.Combine(documents.Folder, profile.Id.ToString("N"));
        Directory.Exists(copies).ShouldBeTrue();
        discard.FailWhen = p => p == Full(h, video.Folder);

        var result = await h.Get<ProfileDeletion>().DeleteAsync(profile.Id, None);

        result.Complete.ShouldBeFalse();
        (await h.Get<IProfileStore>().GetAsync(profile.Id, None)).ShouldNotBeNull();
        Directory.Exists(copies).ShouldBeTrue("the profile stays, so its documents stay with their records");
        (await documents.ListAsync(profile.Id, None)).ShouldHaveSingleItem();

        discard.FailWhen = _ => false;
        (await h.Get<ProfileDeletion>().DeleteAsync(profile.Id, None)).Complete.ShouldBeTrue();
        Directory.Exists(copies).ShouldBeFalse("a second try finishes it");
    }

    [AvaloniaFact]
    public async Task AVideoIsNotDeletedWhileWorkRuns()
    {
        var confirm = new FakeConfirm();
        using var h = await HarnessAsync(confirm: confirm);
        var profile = await ProfileAsync(h, "Byzantium");
        var video = await VideoAsync(h, profile, "Byzantium/greek-fire/v1");
        var session = h.Get<AppSession>();
        await session.SelectProfileAsync(profile, None);
        var page = h.Get<ProducedViewModel>();
        await page.ActivateAsync();
        page.Selected = page.Items.Single();
        var text = h.Get<Localizer>();

        // Running before the delete: nothing is asked.
        session.WorkRunning = true;
        await page.DeleteAsync();
        confirm.Asked.ShouldBeEmpty();
        page.Status.ShouldBe(text["produced.delete.busy"]);
        Directory.Exists(Full(h, video.Folder)).ShouldBeTrue();

        // Started while the confirmation was open: checked again after it.
        session.WorkRunning = false;
        confirm.OnAsk = () => session.WorkRunning = true;
        await page.DeleteAsync();
        confirm.Asked.ShouldHaveSingleItem();
        page.Status.ShouldBe(text["produced.delete.busy"]);
        Directory.Exists(Full(h, video.Folder)).ShouldBeTrue("a new version may be reading its files");
        (await h.Get<IVideoStore>().GetAsync(video.Id, None)).ShouldNotBeNull();
    }

    [AvaloniaFact]
    public async Task AConceptIsNotDeletedFromTheTrashWhileWorkRuns()
    {
        var confirm = new FakeConfirm();
        using var h = await HarnessAsync(confirm: confirm);
        var profile = await ProfileAsync(h, "Byzantium");
        var concept = await ConceptAsync(h, profile, "Greek fire", ConceptStatus.Trashed);
        var video = await VideoAsync(h, profile, "Byzantium/greek-fire/v1", concept.Id);
        var session = h.Get<AppSession>();
        await session.SelectProfileAsync(profile, None);
        var trash = h.Get<TrashViewModel>();
        await trash.ActivateAsync();
        trash.Items.Single().IsChecked = true;
        var text = h.Get<Localizer>();

        session.WorkRunning = true;
        await trash.DeletePermanentlyAsync();
        confirm.Asked.ShouldBeEmpty();
        trash.Status.ShouldBe(text["trash.delete.busy"]);

        session.WorkRunning = false;
        confirm.OnAsk = () => session.WorkRunning = true;
        await trash.DeletePermanentlyAsync();
        confirm.Asked.ShouldHaveSingleItem();
        trash.Status.ShouldBe(text["trash.delete.busy"]);
        (await h.Get<IConceptStore>().GetAsync(concept.Id, None)).ShouldNotBeNull();
        Directory.Exists(Full(h, video.Folder)).ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task AVoiceSampleWhoseNarrationIsInUseStaysAndTheReasonIsShown()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Only Windows refuses to delete a file that is open.");
        }

        using var h = await HarnessAsync();
        var sample = new VoiceSample { Name = "Me", Wav = [1, 2, 3], Sha256 = "x" };
        await h.Get<IVoiceSampleStore>().AddAsync(sample, None);
        var cache = Path.Combine(h.Paths.WorkDirectory, "speech-cache");
        var clip = SpeechService.VoiceTag(VoiceSampleLibrary.VoiceId(sample.Id)) + "-aaaa.wav";
        Put(cache, clip, 5);
        var listen = Path.Combine(h.Paths.WorkDirectory, "listen", "voice-sample.wav");
        Put(Path.GetDirectoryName(listen)!, "voice-sample.wav", 5);
        var page = h.Get<VoiceSettingsViewModel>();
        var text = h.Get<Localizer>();
        var info = new VoiceSampleInfo(sample.Id, sample.Name, 0, DateTime.UtcNow);

        // The system player holds the listen file: it never stops the delete.
        using (new FileStream(listen, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            using (new FileStream(Path.Combine(cache, clip), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                await page.DeleteSampleAsync(info);

                page.SampleStatus.ShouldStartWith(text.Format("voices.samples.remove_failed", "Me", "").Split('')[0]);
                (await h.Get<IVoiceSampleStore>().GetAsync(sample.Id, None)).ShouldNotBeNull("a narration in use keeps the sample");
            }

            await page.DeleteSampleAsync(info);
        }

        page.SampleStatus.ShouldBe(text.Format("voices.samples.removed", "Me"));
        (await h.Get<IVoiceSampleStore>().GetAsync(sample.Id, None)).ShouldBeNull("the listen file in use did not stop it");
        Directory.GetFiles(cache).ShouldBeEmpty();
    }

    private static async Task<DesktopHarness> HarnessAsync(RecordingDiscard? discard = null, FakeConfirm? confirm = null)
    {
        var h = new DesktopHarness(s =>
        {
            discard?.Register(s);
            s.AddSingleton<IConfirmDialog>(confirm ?? new FakeConfirm());
        });
        await FirstRunTests.CreateAsync(h);
        return h;
    }

    private static async Task<Profile> ProfileAsync(DesktopHarness h, string name)
    {
        var profile = new Profile { Name = name, ContentLanguage = ContentLanguages.English, ConceptText = "x" };
        await h.Get<IProfileStore>().SaveAsync(profile, None);
        return profile;
    }

    private static async Task<Concept> ConceptAsync(DesktopHarness h, Profile profile, string title, ConceptStatus status)
    {
        var concept = new Concept { ProfileId = profile.Id, Title = title, Summary = "s", Content = "c", Language = "en", Status = status };
        await h.Get<IConceptStore>().AddAsync([concept], None);
        return concept;
    }

    /// <summary>A video record with a 16-byte video and a 4-byte thumbnail in its folder.</summary>
    private static async Task<VideoRecord> VideoAsync(DesktopHarness h, Profile profile, string folder, Guid? conceptId = null, bool write = true)
    {
        var video = new VideoRecord
        {
            ProfileId = profile.Id, ConceptId = conceptId, Title = folder, Folder = folder, FileName = VideoFolders.VideoFile,
            ThumbnailName = VideoFolders.ThumbnailFile,
        };
        if (write)
        {
            Put(Full(h, folder), VideoFolders.VideoFile, 16);
            Put(Full(h, folder), VideoFolders.ThumbnailFile, 4);
        }

        await h.Get<IVideoStore>().AddAsync(video, [], None);
        return video;
    }

    private static string Full(DesktopHarness h, string relative) => LibraryPaths.Resolve(h.Get<IAppPaths>().LibraryDirectory, relative);

    private static void Put(string folder, string name, int bytes)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, name), new byte[bytes]);
    }
}
