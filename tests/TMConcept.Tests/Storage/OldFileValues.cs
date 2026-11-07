using Shouldly;
using TMConcept.Core.Concepts;
using TMConcept.Core.Profiles;
using TMConcept.Core.Runs;
using TMConcept.Storage.Stores;

namespace TMConcept.Tests.Storage;

/// <summary>The values <see cref="OldFiles"/> hold (Storage/Files/fixture-generator.cs.txt), checked field by field through the stores.</summary>
internal static class OldFileValues
{
    private static readonly CancellationToken None = CancellationToken.None;

    public static Guid KeyId => G(0x0a, 1);

    public static Guid Profile1 => G(0x0b, 1);

    public static Guid Profile2 => G(0x0b, 2);

    public static Guid Concept1 => G(0x0c, 1);

    public static Guid Concept2 => G(0x0c, 2);

    public static Guid Run1 => G(0x0d, 1);

    public static Guid Video1 => G(0x0e, 1);

    /// <summary>The step choices of each profile after the upgrade to version 3, as "profile step provider model key options".</summary>
    public static IReadOnlyList<string> Choices(string file) => file switch
    {
        // 0.1.0–0.1.4: the two kept general defaults became both profiles' choices (the image and voice defaults went).
        OldFiles.Version1 =>
        [
            $"1 code openai gpt-5 - {{}}",
            $"1 concepts anthropic claude-sonnet-5-5 {KeyId} {{\"effort\":\"low\"}}",
            $"2 code openai gpt-5 - {{}}",
            $"2 concepts anthropic claude-sonnet-5-5 {KeyId} {{\"effort\":\"low\"}}",
        ],

        // 0.1.5: profile 1's own choices ("off" included) stayed, the defaults filled the rest.
        OldFiles.Version2 =>
        [
            $"1 code openai gpt-5 - {{}}",
            $"1 concepts openrouter x-model - {{}}",
            $"1 image off  - {{\"imageSource\":\"samples\"}}",
            $"2 code openai gpt-5 - {{}}",
            $"2 concepts anthropic claude-sonnet-5-5 {KeyId} {{\"effort\":\"low\"}}",
        ],
        _ =>
        [
            $"1 concepts openrouter x-model {KeyId} {{}}",
            $"2 code openai gpt-5 - {{\"effort\":\"high\"}}",
        ],
    };

    /// <summary>Every table's rows of <paramref name="file"/> read back with this version's stores.</summary>
    public static async Task ShouldHoldEverythingAsync(StorageHarness h, string file)
    {
        var key = (await new ApiKeyStore(h.Database).ListAsync(null, None)).ShouldHaveSingleItem();
        (key.Id, key.Provider, key.Label, key.Secret, key.CreatedAtUtc, key.LastOkAtUtc).ShouldBe((KeyId, "anthropic", "Main key", "sealed-secret", T(1, 8), T(2, 9, 30)));

        var profiles = new ProfileStore(h.Database);
        var all = await profiles.ListAsync(None);
        all.Select(p => p.Name).ShouldBe(["Byzantium", "Tech"]);
        var p1 = all[0];
        (p1.Id, p1.ContentLanguage, p1.Format, p1.TargetSeconds, p1.ConceptText, p1.TemplateId, p1.ConceptCounter, p1.CreatedAtUtc, p1.UpdatedAtUtc)
            .ShouldBe((Profile1, "tr", VideoFormat.Horizontal, 90, "Byzantine history", "history-byzantium", 2, T(1, 8), T(3, 10)));
        var p2 = all[1];
        (p2.Id, p2.ContentLanguage, p2.Format, p2.TargetSeconds, p2.TemplateId, p2.ConceptCounter).ShouldBe((Profile2, "en", VideoFormat.Vertical, 60, (string?)null, 0));

        var image = (await profiles.ImagesAsync(Profile1, None)).ShouldHaveSingleItem();
        (image.FileName, image.Caption, image.Width, image.Height, image.Sha256, image.AddedAtUtc)
            .ShouldBe(("profiles/byzantium/hagia.png", "Hagia Sophia", 1024, 768, "ab12cd34ef56ab12cd34ef56ab12cd34ef56ab12cd34ef56ab12cd34ef56ab12", T(2, 11)));

        var concepts = new ConceptStore(h.Database);
        var listed = await concepts.ListAsync(Profile1, [ConceptStatus.Approved, ConceptStatus.Trashed], withContent: true, None);
        listed.Select(c => c.Number).ShouldBe([2, 1]);
        var c2 = listed[0];
        (c2.Id, c2.Status, c2.Title, c2.RunId, c2.SimilarToId, c2.Similarity, c2.DuplicateNote, c2.DecidedAtUtc)
            .ShouldBe((Concept2, ConceptStatus.Trashed, "The last emperor", (Guid?)Run1, (Guid?)Concept1, (double?)0.71, "similar to #1 (71 %)", (DateTimeOffset?)null));
        var c1 = listed[1];
        (c1.Id, c1.ProfileId, c1.Status, c1.Summary, c1.Content, c1.Language, c1.SimilarToId, c1.Similarity, c1.CreatedAtUtc, c1.DecidedAtUtc)
            .ShouldBe((Concept1, Profile1, ConceptStatus.Approved, "1453. The walls fall.", "Line one\n[on screen: walls]\nLine two", "tr", (Guid?)null, (double?)null, T(5, 9),
                (DateTimeOffset?)T(5, 10)));
        (await concepts.ListAsync(Profile1, [ConceptStatus.Approved], withContent: false, None)).ShouldHaveSingleItem().Content.ShouldBeEmpty();
        (await concepts.HistoryAsync(Profile1, None)).Select(s => (s.Id, s.Number, s.Title)).ShouldBe([(Concept1, 1, "Fall of Constantinople"), (Concept2, 2, "The last emperor")]);
        (await concepts.CountAsync(Profile1, ConceptStatus.Trashed, None)).ShouldBe(1);

        var embeddings = new EmbeddingStore(h.Database);
        var embedding = (await embeddings.ListAsync(Profile1, "text-embedding-3-small", None)).ShouldHaveSingleItem();
        (embedding.ConceptId, embedding.Dimensions).ShouldBe((Concept1, 2));
        embedding.Vector.ShouldBe(new byte[] { 0, 0, 128, 63, 0, 0, 0, 64 });
        var digest = (await embeddings.DigestAsync(Profile1, None)).ShouldNotBeNull();
        (digest.Version, digest.Text, digest.CoversUntilNumber, digest.UpdatedAtUtc).ShouldBe((2, "Covered: 1453.", 2, T(6, 7)));

        var runs = await new RunStore(h.Database).ListAsync(T(1, 0), None);
        runs.Count.ShouldBe(2);
        var r2 = runs[0];
        (r2.ProfileId, r2.Kind, r2.Status, r2.Error, r2.CostExact, r2.ActualUsd, r2.FinishedAtUtc).ShouldBe(((Guid?)null, RunKind.ConnectionTest, RunStatus.Failed, "timeout", false, 0m,
            (DateTimeOffset?)null));
        var r1 = runs[1];
        (r1.Id, r1.ProfileId, r1.Kind, r1.Provider, r1.Model, r1.KeyLabel, r1.Requested, r1.Accepted, r1.RejectedDuplicates, r1.ExtraPrompt)
            .ShouldBe((Run1, (Guid?)Profile1, RunKind.Concepts, "anthropic", "claude-sonnet-5-5", "Main key", 2, 2, 1, "more battles"));
        (r1.EstimateMinUsd, r1.EstimateMaxUsd, r1.ActualUsd, r1.CostExact, r1.InputTokens, r1.CachedTokens, r1.CacheWriteTokens, r1.OutputTokens)
            .ShouldBe((0.01m, 0.0425m, 0.0123m, true, 1200L, 300L, 100L, 900L));
        (r1.StartedAtUtc, r1.FinishedAtUtc, r1.Status, r1.Error).ShouldBe((T(5, 8, 59), (DateTimeOffset?)T(5, 9), RunStatus.Succeeded, ""));

        var queue = (await new QueueStore(h.Database).ListAsync(Profile1, None)).ShouldHaveSingleItem();
        (queue.ConceptId, queue.Position, queue.SettingsJson, queue.AddedAtUtc).ShouldBe((Concept1, 0, "{\"subtitles\":true}", T(5, 11)));

        var videos = new VideoStore(h.Database);
        var video = (await videos.ListAsync(Profile1, None)).ShouldHaveSingleItem();
        (video.Id, video.ConceptId, video.Title, video.Folder, video.FileName, video.ThumbnailName, video.DurationMs, video.Width, video.Height, video.Fps, video.SizeBytes)
            .ShouldBe((Video1, (Guid?)Concept1, "Fall of Constantinople", "videos/byzantium", "fall.mp4", "fall.jpg", 75000L, 1920, 1080, 30, 12345678L));
        (video.Engine, video.EngineVersion, video.CodeModel, video.ConceptModel, video.SpeechModel, video.ImageModel, video.SettingsJson, video.PromptVersion)
            .ShouldBe(("remotion", "4.0.1", "claude-opus-5-5", "claude-sonnet-5-5", "piper", "flux", "{\"subtitles\":true}", "3"));
        (video.TotalUsd, video.CostExact, video.ElapsedMs, video.Warnings, video.CreatedAtUtc).ShouldBe((0.5m, true, 61000L, "no voice", T(6, 20)));
        (await videos.CostsAsync(Video1, None)).OrderBy(c => c.Step, StringComparer.Ordinal).Select(c => (c.Step, c.RunId, c.Usd, c.Exact))
            .ShouldBe([("code", (Guid?)Run1, 0.25m, true), ("speech", (Guid?)null, 0m, false)]);

        var settings = await new SettingsStore(h.Database).GetAsync(None);
        (settings.Language, settings.NotificationsEnabled, settings.NotificationSeconds).ShouldBe(("tr", false, 30));

        var steps = new StepModelStore(h.Database);
        var choices = new List<string>();
        foreach (var (number, id) in new[] { (1, Profile1), (2, Profile2) })
        {
            choices.AddRange((await steps.ListAsync(id, None)).Select(c => $"{number} {c.Step} {c.Provider} {c.Model} {c.KeyId?.ToString() ?? "-"} {c.OptionsJson}"));
        }

        choices.Order(StringComparer.Ordinal).ShouldBe(Choices(file));
    }

    private static Guid G(int kind, int n) => Guid.Parse($"{kind:x2}000000-0000-0000-0000-{n:x12}");

    private static DateTimeOffset T(int day, int hour, int minute = 0) => new(2026, 10, day, hour, minute, 0, TimeSpan.Zero);
}
