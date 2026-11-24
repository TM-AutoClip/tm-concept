using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Channels;
using TMConcept.Core.Chat;
using TMConcept.Core.Costs;
using TMConcept.Core.DiskSpace;
using TMConcept.Core.Keys;
using TMConcept.Core.Models;
using TMConcept.Core.Music;
using TMConcept.Core.Profiles;
using TMConcept.Core.Providers;
using TMConcept.Core.References;
using TMConcept.Core.Runs;
using TMConcept.Core.Speech;
using TMConcept.Core.Styles;
using TMConcept.Tests.Providers;
using TMConcept.Tests.Storage;

namespace TMConcept.Tests.Core;

/// <summary>
/// "Kanaldan esinlen" (0.1.53) without a network and without yt-dlp: a saved yt-dlp <c>-J --flat-playlist</c> answer is read, the samples are
/// picked shorter first within the limits, the measured samples are merged by their median, the target length, format and language come from
/// the listing, the one model call carries the no-copy rule and is the only call, the temporary files go after a failure and a cancel (a fake
/// download), a full template list only warns, and existing templates never change.
/// </summary>
public sealed class ChannelInspirationTests
{
    /// <summary>The shape of yt-dlp's answer for a YouTube channel's Videos tab (ids, names and texts made up).</summary>
    private const string VideosTab = """
        {"id": "UCxxxxxxxxxxxxxxxxxxxxxx", "channel": "Tarih Kutusu", "channel_id": "UCxxxxxxxxxxxxxxxxxxxxxx", "title": "Tarih Kutusu - Videos",
         "description": "Osmanlı ve Bizans tarihi «kısa» anlatımlar.\nHer hafta yeni video.", "uploader": "Tarih Kutusu",
         "thumbnails": [
           {"url": "https://yt3.example.com/banner=w1060", "id": "banner_uncropped", "width": 1060, "height": 175},
           {"url": "https://yt3.example.com/avatar=s900", "id": "avatar_uncropped", "width": 900, "height": 900},
           {"url": "http://yt3.example.com/plain", "id": "7", "width": 88, "height": 88}],
         "_type": "playlist", "webpage_url": "https://www.youtube.com/@tarihkutusu/videos",
         "entries": [
           {"_type": "url", "ie_key": "Youtube", "id": "aaaaaaaaaaa", "url": "https://www.youtube.com/watch?v=aaaaaaaaaaa",
            "title": "İstanbul neden bu kadar önemliydi?", "description": "Bu videoda şehrin hikâyesi var.", "duration": 612.0, "upload_date": "20261120"},
           {"_type": "url", "ie_key": "Youtube", "id": "bbbbbbbbbbb", "url": "https://www.youtube.com/watch?v=bbbbbbbbbbb",
            "title": "Bizans surları nasıl yıkıldı?", "description": null, "duration": 58.0, "timestamp": 1763000000},
           {"_type": "url", "ie_key": "Youtube", "id": "ccccccccccc", "url": "https://www.youtube.com/shorts/ccccccccccc",
            "title": "Osmanlı ve Bizans: bir günde ne değişti?", "view_count": 1200},
           {"_type": "url", "ie_key": "Youtube", "id": "ddddddddddd", "url": "https://www.youtube.com/watch?v=ddddddddddd",
            "title": "Canlı yayın: soru cevap", "duration": null, "live_status": "is_live"},
           {"_type": "url", "ie_key": "Youtube", "id": "eeeeeeeeeee", "url": "javascript:alert(1)", "title": "Bad address", "duration": 30},
           {"_type": "url", "ie_key": "Youtube", "id": "fffffffffff", "url": "https://www.youtube.com/watch?v=fffffffffff",
            "title": "Kuşatmanın son günü ve bir şehir", "duration": 95.5, "width": 1920, "height": 1080}
         ]}
        """;

    /// <summary>A channel's home: yt-dlp lists its tabs, not its videos.</summary>
    private const string Home = """
        {"id": "UCyyyy", "title": "Some Channel", "description": "", "_type": "playlist",
         "entries": [
           {"_type": "url", "ie_key": "YoutubeTab", "id": "UCyyyy", "url": "https://www.youtube.com/@some/videos", "title": "Some Channel - Videos"},
           {"_type": "url", "ie_key": "YoutubeTab", "id": "UCyyyy", "url": "https://www.youtube.com/@some/shorts", "title": "Some Channel - Shorts"}]}
        """;

    [Fact]
    public void ASavedYtDlpAnswerIsRead()
    {
        var listing = ChannelListingReader.Read(VideosTab, ChannelLimits.ListCount);

        listing.Name.ShouldBe("Tarih Kutusu");
        listing.Description.ShouldStartWith("Osmanlı ve Bizans tarihi \"kısa\"", Case.Sensitive, "« » cannot close a data block in the prompt");
        listing.PhotoUrl.ShouldBe("https://yt3.example.com/avatar=s900", "the avatar, never the banner or a plain http picture");
        listing.Tabs.ShouldBeEmpty();
        listing.Videos.Select(v => v.Id).ShouldBe(["aaaaaaaaaaa", "bbbbbbbbbbb", "ccccccccccc", "ddddddddddd", "fffffffffff"], "an address that is not http(s) is left out");
        listing.Videos[0].Seconds.ShouldBe(612);
        listing.Videos[0].Uploaded.ShouldBe(new DateOnly(2026, 11, 20));
        listing.Videos[0].Description.ShouldBe("Bu videoda şehrin hikâyesi var.");
        listing.Videos[1].Uploaded.ShouldNotBeNull();
        listing.Videos[1].Description.ShouldBeEmpty();
        listing.Videos[2].Seconds.ShouldBeNull();
        listing.Videos[3].Live.ShouldBeTrue();
        listing.Videos[4].Width.ShouldBe(1920);
        ChannelListingReader.Read(VideosTab, 2).Videos.Count.ShouldBe(2, "at most the asked number");

        var home = ChannelListingReader.Read(Home, ChannelLimits.ListCount);
        home.Name.ShouldBe("Some Channel", "the title without its tab tail");
        home.Videos.ShouldBeEmpty();
        home.Tabs.ShouldBe(["https://www.youtube.com/@some/videos", "https://www.youtube.com/@some/shorts"]);

        ChannelListingReader.JsonLine(["WARNING: [youtube] something", "{\"id\": 1}", "done"]).ShouldBe("{\"id\": 1}");
        Should.Throw<ChannelInspireException>(() => ChannelListingReader.Read("ERROR: not json", 30)).Code.ShouldBe("list");
    }

    [Fact]
    public void SamplesAreShorterFirstWithinTheLimits()
    {
        var videos = new List<ChannelVideo>
        {
            Video("a", 600), Video("b", 40), Video("c", null), Video("d", 3), Video("e", 90), Video("f", 20, live: true), Video("g", 40, url: "https://example.com/b"),
            Video("h", null),
        };

        ChannelSamplePicker.Pick(videos, 3).Select(v => v.Id).ShouldBe(["b", "e", "a"], "shortest first; too short, live and repeated addresses left out");
        ChannelSamplePicker.Pick(videos, 5).Select(v => v.Id).ShouldBe(["b", "e", "a", "c", "h"], "a length that is not listed comes last, in order");
        ChannelSamplePicker.Pick(videos, 99).Count.ShouldBe(5, "at most 5");
        ChannelSamplePicker.Pick(videos, 0).Count.ShouldBe(1, "at least 1");
        ChannelSamplePicker.Pick([], 3).ShouldBeEmpty();
    }

    [Fact]
    public void MeasuredSamplesAreMergedByTheirMedian()
    {
        var merged = TempoProfileMerger.Merge(
        [
            Profile(2, 0.2, CameraDirection.Left, 100, ["#111111"]),
            Profile(9, 0.9, CameraDirection.Right, 0, ["#999999"]),
            Profile(4, 0.4, CameraDirection.Left, 120, ["#444444"]),
        ], "https://example.com/@channel", DateTimeOffset.UnixEpoch);

        merged.CutsPer10Seconds.ShouldBe(4, "the median, not the mean (5)");
        merged.CameraShare.ShouldBe(0.4);
        merged.CameraDirection.ShouldBe(CameraDirection.Left, "the most common way");
        merged.Bpm.ShouldBe(110, "the median of the samples with a beat");
        merged.Palette.ShouldBe(["#444444"], "the colours of the sample nearest the median");
        merged.Source.ShouldBe("https://example.com/@channel");
        merged.FromUrl.ShouldBeTrue();
        TempoProfileMerger.Median([1, 2, 3, 4]).ShouldBe(2.5);
        Should.Throw<ArgumentException>(() => TempoProfileMerger.Merge([], "x", DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void TheTargetLengthFormatAndLanguageComeFromTheListing()
    {
        var listing = ChannelListingReader.Read(VideosTab, ChannelLimits.ListCount);
        var facts = ChannelDraftBuilder.Facts(listing);
        facts.TargetSeconds.ShouldBe(95, "the median of 612, 58 and 95.5 (the live one left out), rounded to 5");
        facts.Language.ShouldBe(ContentLanguages.Turkish);

        ChannelDraftBuilder.TargetSeconds([Video("a", 3), Video("b", 4)]).ShouldBe(ProfileLimits.SecondsMin, "within the profile's limits");
        ChannelDraftBuilder.TargetSeconds([Video("a", 3000)]).ShouldBe(ProfileLimits.SecondsMax);
        ChannelDraftBuilder.TargetSeconds([Video("a", null)]).ShouldBeNull();

        ChannelDraftBuilder.Format([Video("a", 30, url: "https://www.youtube.com/shorts/x"), Video("b", 30, width: 1080, height: 1920), Video("c", 300, width: 1920, height: 1080)])
            .ShouldBe(VideoFormat.Vertical);
        ChannelDraftBuilder.Format([Video("a", 30, url: "https://www.tiktok.com/@x/video/1")]).ShouldBe(VideoFormat.Vertical);
        ChannelDraftBuilder.Format([Video("a", 300, width: 1280, height: 720)]).ShouldBe(VideoFormat.Horizontal);
        ChannelDraftBuilder.Format([Video("a", 300), Video("b", 300)]).ShouldBeNull("a watch address does not tell");
        ChannelDraftBuilder.Format([Video("a", 30, width: 1080, height: 1920), Video("b", 300, width: 1920, height: 1080)]).ShouldBeNull("a tie");

        ChannelLanguageGuess.Guess(["How the walls of the city fell", "Why was the empire so rich?", "What is the story of the sultan", "Who built the tower"])
            .ShouldBe(ContentLanguages.English);
        ChannelLanguageGuess.Guess(["Wie die Mauern fielen", "Warum war das Reich so reich und mächtig", "Der Turm und die Stadt"]).ShouldBe(ContentLanguages.German);
        ChannelLanguageGuess.Guess(["Konstantinopolis", "Bizans 1453"]).ShouldBeNull("unsure: the user chooses");
        ChannelLanguageGuess.Guess([]).ShouldBeNull();
    }

    [Fact]
    public async Task TheModelIsCalledOnceWithTheNoCopyRule()
    {
        var ct = TestContext.Current.CancellationToken;
        var inspiration = Inspiration();
        var chat = new FakeChat().Answer(_ => new ChatResponse("{\"concept_text\": \"Calm history narration for curious adults.\"}", new ChatUsage(900, 0, 0, 300), null,
            "end_turn", "req-1"));
        var runs = new MemoryRuns();
        var writer = new ChannelStyleWriter(chat, new CostCalculator(), runs, TimeProvider.System, NullLogger<ChannelStyleWriter>.Instance);

        var result = await writer.WriteAsync(inspiration, Model(), ContentLanguages.Turkish, null, ct);

        chat.Requests.Count.ShouldBe(1, "one call");
        var request = chat.Requests[0];
        request.System.ShouldContain(ChannelPromptBuilder.NoCopyRule(ContentLanguages.Turkish));
        request.User.ShouldContain("«İstanbul neden bu kadar önemliydi?»", Case.Sensitive, "titles go as data between « »");
        request.Schema.ShouldNotBeNull();
        result.ConceptText.ShouldBe("Calm history narration for curious adults.");
        result.CostUsd.ShouldBeGreaterThan(0);
        runs.Added.Single().Kind.ShouldBe(RunKind.ChannelInspiration);
        runs.Added.Single().Status.ShouldBe(RunStatus.Succeeded);
        runs.Added.Single().ProfileId.ShouldBeNull();
        ChannelPromptBuilder.Build(inspiration, ContentLanguages.English).System.ShouldContain(ChannelPromptBuilder.NoCopyRule(ContentLanguages.English));
        writer.Estimate(Model().Model, ContentLanguages.English).MaxUsd.ShouldBeGreaterThan(0);

        // An answer without content is not asked again: still one call, a failed run.
        var empty = new FakeChat().Answer(_ => new ChatResponse("{\"other\": 1}", new ChatUsage(900, 0, 0, 10), null, "end_turn", "req-2"));
        var emptyRuns = new MemoryRuns();
        var second = new ChannelStyleWriter(empty, new CostCalculator(), emptyRuns, TimeProvider.System, NullLogger<ChannelStyleWriter>.Instance);
        (await Should.ThrowAsync<ChannelInspireException>(() => second.WriteAsync(inspiration, Model(), ContentLanguages.English, null, ct))).Code.ShouldBe("answer");
        empty.Requests.Count.ShouldBe(1);
        emptyRuns.Added.Single().Status.ShouldBe(RunStatus.Failed);

        // A cut answer keeps the content written before the cut.
        ChannelStyleWriter.Content("{\"concept_text\": \"Calm narration \\\"quoted\\\" and more").ShouldBe("Calm narration \"quoted\" and more");
        var cut = new FakeChat().Answer(_ => throw new ProviderException("cut")
        {
            Code = "truncated", ChargedUsage = new ChatUsage(900, 0, 0, 6000), PartialText = "{\"concept_text\": \"Half a text",
        });
        var cutRuns = new MemoryRuns();
        var third = new ChannelStyleWriter(cut, new CostCalculator(), cutRuns, TimeProvider.System, NullLogger<ChannelStyleWriter>.Instance);
        (await third.WriteAsync(inspiration, Model(), ContentLanguages.English, null, ct)).ConceptText.ShouldBe("Half a text");
        cut.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task TemporaryFilesGoAfterASuccessAFailureAndACancel()
    {
        var ct = TestContext.Current.CancellationToken;
        using var paths = new TempPaths();
        var downloader = new FakeDownloader();
        var reader = new FakeReader();
        var measurer = new ReferenceMeasurer(paths, reader, new FakeAudio(), downloader, TimeProvider.System, NullLogger<ReferenceMeasurer>.Instance);
        var lister = new FakeLister();
        var inspirer = new ChannelInspirer(paths, lister, measurer, new FakePhotos(), TimeProvider.System, NullLogger<ChannelInspirer>.Instance);
        var references = Path.Combine(paths.WorkDirectory, ReferenceLimits.Folder);
        var channels = Path.Combine(paths.WorkDirectory, ChannelLimits.Folder);

        var stages = new List<string>();
        var done = await inspirer.InspectAsync(" https://www.youtube.com/@tarihkutusu/videos ", 2, photo: true, new Sync<ChannelProgress>(p => stages.Add(p.Stage)), ct);
        done.Measured.ShouldBe(2);
        done.Failed.ShouldBe(0);
        done.Tempo.ShouldNotBeNull().Source.ShouldBe("https://www.youtube.com/@tarihkutusu/videos");
        downloader.Addresses.ShouldBe(["https://www.youtube.com/watch?v=bbbbbbbbbbb", "https://www.youtube.com/watch?v=fffffffffff"], "the two shortest");
        stages.ShouldContain(ChannelStages.List);
        stages.ShouldContain(ChannelStages.Sample);
        stages.ShouldContain(ChannelStages.Photo);
        Directory.EnumerateFileSystemEntries(references).ShouldBeEmpty("no downloaded video stays");
        File.Exists(done.PhotoFile).ShouldBeTrue("the photo stays until the preview closes");
        inspirer.ActiveFolders.ShouldBe([done.Folder]);
        inspirer.Discard(done);
        Directory.Exists(done.Folder).ShouldBeFalse();
        inspirer.ActiveFolders.ShouldBeEmpty();

        // Every sample fails: the look goes on with the listing only, and nothing downloaded stays.
        reader.Fail = true;
        var failed = await inspirer.InspectAsync("https://www.youtube.com/@tarihkutusu/videos", 3, photo: false, null, ct);
        failed.Measured.ShouldBe(0);
        failed.Failed.ShouldBe(3);
        failed.Tempo.ShouldBeNull();
        failed.PhotoFile.ShouldBeNull();
        Directory.EnumerateFileSystemEntries(references).ShouldBeEmpty("a failure deletes the download too");
        inspirer.Discard(failed);

        // A cancel during a download: nothing stays, not even the look's own folder.
        reader.Fail = false;
        downloader.Cancel = true;
        await Should.ThrowAsync<OperationCanceledException>(() => inspirer.InspectAsync("https://www.youtube.com/@tarihkutusu/videos", 2, photo: true, null, ct));
        Directory.EnumerateFileSystemEntries(references).ShouldBeEmpty("a cancel deletes the part that was downloaded");
        Directory.EnumerateFileSystemEntries(channels).ShouldBeEmpty("a cancel deletes the look's folder");
        inspirer.ActiveFolders.ShouldBeEmpty();

        // yt-dlp cannot read the channel: the look's folder goes as well.
        downloader.Cancel = false;
        lister.Fail = true;
        (await Should.ThrowAsync<ChannelInspireException>(() => inspirer.InspectAsync("https://example.com/@x", 2, photo: false, null, ct))).Code.ShouldBe("list");
        Directory.EnumerateFileSystemEntries(channels).ShouldBeEmpty();
        (await Should.ThrowAsync<ChannelInspireException>(() => inspirer.InspectAsync("ftp://example.com/@x", 2, photo: false, null, ct))).Code.ShouldBe("address");

        // A channel's home: its tabs are read instead.
        lister.Fail = false;
        lister.HomeFirst = true;
        var home = await inspirer.InspectAsync("https://www.youtube.com/@some", 1, photo: false, null, ct);
        home.Listing.Videos.ShouldNotBeEmpty();
        lister.Addresses.ShouldContain("https://www.youtube.com/@some/videos");
        inspirer.Discard(home);

        // A later tab yt-dlp cannot read (the Shorts tab) keeps the videos the first one gave; a first tab that fails still fails the look.
        lister.FailShorts = true;
        var kept = await inspirer.InspectAsync("https://www.youtube.com/@some", 1, photo: false, null, ct);
        kept.Listing.Videos.ShouldNotBeEmpty();
        lister.Addresses.ShouldContain("https://www.youtube.com/@some/shorts");
        inspirer.Discard(kept);
    }

    [Fact]
    public void TheStartUpSweepLeavesTheRunningLooks()
    {
        var work = Path.Combine(TestFolders.Root, "work-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(work, ChannelLimits.Folder);
        var old = Path.Combine(root, "old");
        var running = Path.Combine(root, "running");
        new LeftoverSweep().ChannelTemps(work, [old, running, Path.Combine(work, "reference", "x"), root], [running]).ShouldBe([Path.GetFullPath(old)]);
    }

    [Fact]
    public async Task AFullTemplateListOnlyWarnsAndExistingTemplatesNeverChange()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new MemoryStyles();
        var first = new VideoStyle { Name = "Mine", Tempo = NarrationTempo.Relaxed };
        await store.SaveAsync(first, ct);
        var creator = new ChannelTemplateCreator(store, TimeProvider.System, NullLogger<ChannelTemplateCreator>.Instance);
        var tempo = Profile(6.5, 0.7, CameraDirection.Left, 128, ["#202830"]);

        var draft = ChannelTemplateCreator.Draft("Tarih Kutusu esinli " + new string('x', 80), tempo, DateTimeOffset.UnixEpoch);
        draft.Name.Length.ShouldBe(VideoStyleLimits.NameMax);
        draft.Tempo.ShouldBe(NarrationTempo.Tight, "6.5 cuts every 10 s is nearest to tight");
        draft.Reference.ShouldBeSameAs(tempo);
        draft.Drawer.ShouldBe(SceneDrawer.App);
        VideoStyleRules.Problems(draft).ShouldBeEmpty();

        // An id that clashes with an existing template never overwrites it.
        draft.Id = first.Id;
        (await creator.CreateAsync(draft, ct)).ShouldBe(ChannelTemplateOutcome.Created);
        store.Styles.Count.ShouldBe(2);
        var kept = store.Styles.Single(s => s.Id == first.Id);
        kept.Name.ShouldBe("Mine");
        kept.Tempo.ShouldBe(NarrationTempo.Relaxed);
        kept.Reference.ShouldBeNull();

        while (store.Styles.Count < VideoStyleLimits.Max)
        {
            await store.SaveAsync(new VideoStyle { Name = "T" + store.Styles.Count }, ct);
        }

        (await creator.HasRoomAsync(ct)).ShouldBeFalse();
        (await creator.CreateAsync(ChannelTemplateCreator.Draft("Another", tempo, DateTimeOffset.UnixEpoch), ct)).ShouldBe(ChannelTemplateOutcome.LimitReached);
        store.Styles.Count.ShouldBe(VideoStyleLimits.Max, "nothing written at the limit");
    }

    internal static ChannelInspiration Inspiration()
    {
        var listing = ChannelListingReader.Read(VideosTab, ChannelLimits.ListCount);
        return new ChannelInspiration(listing, ChannelDraftBuilder.Facts(listing), Profile(4, 0.4, CameraDirection.Left, 110, ["#444444"]), 2, 0, null, string.Empty);
    }

    internal static ResolvedModel Model() => new(new CatalogModel
    {
        Provider = "anthropic", Id = "claude-haiku-5-5", Name = "Claude Haiku 5.5", Capabilities = ModelCapability.Chat | ModelCapability.Schema, InputUsd = 1,
        OutputUsd = 5, MaxOutputTokens = 64000,
    }, new ApiKey { Provider = "anthropic", Label = "test", Secret = "k" }, string.Empty);

    internal static ReferenceTempoProfile Profile(double cuts, double camera, CameraDirection direction, double bpm, List<string> palette) => new()
    {
        Seconds = 60, Cuts = (int)(cuts * 6), CutsPer10Seconds = cuts, AverageShotSeconds = 10 / Math.Max(cuts, 0.1), CameraShare = camera, CameraDirection = direction,
        Ease = EaseShape.Out, Brightness = 0.5, Saturation = 0.4, Palette = palette, HasSound = bpm > 0, Bpm = bpm, Peaks = [0.5],
    };

    private static ChannelVideo Video(string id, double? seconds, bool live = false, string? url = null, int width = 0, int height = 0) =>
        new(id, url ?? "https://example.com/" + id, "Title " + id, seconds, string.Empty, null, width, height, live);

    private sealed class Sync<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class FakeLister : IChannelLister
    {
        public bool Fail { get; set; }

        public bool HomeFirst { get; set; }

        public bool FailShorts { get; set; }

        public List<string> Addresses { get; } = [];

        public bool Ready => true;

        public Task<ChannelListing> ListAsync(Uri address, int count, string folder, CancellationToken cancellationToken)
        {
            Directory.Exists(folder).ShouldBeTrue("yt-dlp runs in the look's own folder");
            Addresses.Add(address.AbsoluteUri);
            if (Fail || (FailShorts && address.AbsolutePath.EndsWith("/shorts", StringComparison.Ordinal)))
            {
                throw new ChannelInspireException("no") { Code = "list" };
            }

            if (HomeFirst && !address.AbsolutePath.EndsWith("/videos", StringComparison.Ordinal) && !address.AbsolutePath.EndsWith("/shorts", StringComparison.Ordinal))
            {
                return Task.FromResult(ChannelListingReader.Read(Home, count));
            }

            return Task.FromResult(ChannelListingReader.Read(VideosTab, count));
        }
    }

    private sealed class FakePhotos : IChannelPhotoFetcher
    {
        public async Task<string> FetchAsync(Uri photo, string folder, CancellationToken cancellationToken)
        {
            var file = Path.Combine(folder, "photo.png");
            await File.WriteAllTextAsync(file, "png", cancellationToken);
            return file;
        }
    }

    private sealed class FakeDownloader : IReferenceDownloader
    {
        public bool Cancel { get; set; }

        public List<string> Addresses { get; } = [];

        public bool Ready => true;

        public async Task<string> DownloadAsync(Uri address, string folder, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            Addresses.Add(address.AbsoluteUri);
            var file = Path.Combine(folder, "reference.mkv");
            await File.WriteAllTextAsync(file, "video", cancellationToken);
            if (Cancel)
            {
                await File.WriteAllTextAsync(file + ".part", "half", cancellationToken);
                throw new OperationCanceledException();
            }

            return file;
        }
    }

    private sealed class FakeReader : IReferenceVideoReader
    {
        public bool Fail { get; set; }

        public Task<ReferenceFrameSeries?> ReadFramesAsync(string video, string work, CancellationToken cancellationToken)
        {
            const int count = 60;
            var series = new ReferenceFrameSeries(10, [.. Enumerable.Range(0, count).Select(i => i % 20 == 0 && i > 0 ? 0.5 : 0.001)],
                [.. Enumerable.Range(0, count).Select(i => i % 20 == 0 && i > 0 ? 0.9 : 0.0)], [.. Enumerable.Range(0, count).Select(i => i % 20 == 0 && i > 0 ? 0.1 : 1.0)],
                [.. Enumerable.Repeat(0.0, count)], [.. Enumerable.Repeat(0.0, count)], [.. Enumerable.Repeat(false, count)]);
            return Task.FromResult(Fail ? null : series);
        }

        public Task<ReferenceColors?> ReadColorsAsync(string video, string work, CancellationToken cancellationToken) =>
            Task.FromResult<ReferenceColors?>(new ReferenceColors(50, 10, -5, 0.5, 0.4, ["#884422"]));
    }

    private sealed class FakeAudio : IMusicAudio
    {
        public Task<float[]?> ReadMonoAsync(string file, int sampleRate, int maxSeconds, string work, CancellationToken cancellationToken) =>
            Task.FromResult<float[]?>(null);

        public Task<bool> RenderBedAsync(string file, MusicBed bed, string output, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    internal sealed class MemoryRuns : IRunStore
    {
        public List<Run> Added { get; } = [];

        public Task AddAsync(Run run, CancellationToken cancellationToken)
        {
            Added.Add(run);
            return Task.CompletedTask;
        }

        public Task<Run?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Added.FirstOrDefault(r => r.Id == id));

        public Task UpdateAsync(Run run, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<Run>> ListAsync(DateTimeOffset sinceUtc, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Run>>(Added);

        public Task<double> AverageConceptOutputTokensAsync(Guid profileId, CancellationToken cancellationToken) => Task.FromResult(0d);

        public Task<IReadOnlyList<Run>> ConceptEditsNotInVideosAsync(Guid conceptId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Run>>([]);
    }

    internal sealed class MemoryStyles : IVideoStyleStore
    {
        public List<VideoStyle> Styles { get; } = [];

        public Task<IReadOnlyList<VideoStyle>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<VideoStyle>>([.. Styles.Select(VideoStyleRules.Copy)]);

        public Task<VideoStyle?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Styles.FirstOrDefault(s => s.Id == id) is { } s ? VideoStyleRules.Copy(s) : null);

        public Task SaveAsync(VideoStyle style, CancellationToken cancellationToken)
        {
            var index = Styles.FindIndex(s => s.Id == style.Id);
            if (index >= 0)
            {
                Styles[index] = VideoStyleRules.Copy(style);
                return Task.CompletedTask;
            }

            if (Styles.Count >= VideoStyleLimits.Max)
            {
                throw new VideoStyleLimitException();
            }

            Styles.Add(VideoStyleRules.Copy(style));
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            Styles.RemoveAll(s => s.Id == id);
            return Task.CompletedTask;
        }
    }
}
