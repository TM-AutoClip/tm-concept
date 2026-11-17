using Shouldly;
using TMConcept.Core.Concepts;
using TMConcept.Core.Profiles;
using TMConcept.Core.Videos;

namespace TMConcept.Tests.Core;

/// <summary>
/// 0.1.18 (the developer, 17 Nov 2026): a liked video is a positive example, a disliked one the strongest "stay away" of the topic and concept
/// prompts; a produced but unrated video is only covered (producing is no like).
/// </summary>
public sealed class RatingPromptTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    private static Profile Profile(string language) => new()
    {
        Name = "Byzantium",
        ContentLanguage = language,
        Format = VideoFormat.Vertical,
        TargetSeconds = 60,
        ConceptText = "A thousand years of Byzantium.",
    };

    private static Concept Of(int number, string title, ConceptStatus status = ConceptStatus.Produced) =>
        new() { Number = number, Title = title, Summary = "s", Status = status };

    private static VideoRating Rated(string title, VideoVote vote, int minutes, Guid? conceptId = null, string summary = "Two sentences. About it.") =>
        new() { Title = title, Summary = summary, Vote = vote, RatedAtUtc = At.AddMinutes(minutes), ConceptId = conceptId };

    [Fact]
    public void LikedAndDislikedVideosGoToTheTopicAndConceptPromptsInBothLanguages()
    {
        var liked = Of(1, "Hagia Sophia");
        var disliked = Of(2, "Iconoclasm");
        var memory = PromptMemoryBuilder.Build([liked, disliked, Of(3, "Basil II")], [], ContentLanguages.English, null,
            [Rated("Hagia Sophia", VideoVote.Like, 1, liked.Id), Rated("Iconoclasm", VideoVote.Dislike, 2, disliked.Id, string.Empty)]);

        memory.LikedVideos.ShouldBe("\n- Hagia Sophia: Two sentences. About it.");
        memory.DislikedVideos.ShouldBe("\n- Iconoclasm", "no summary: the title alone");
        memory.Covered.ShouldNotBeNull().ShouldNotContain("Iconoclasm", Case.Sensitive, "a disliked video is listed once, as the strong negative");
        memory.Covered.ShouldContain("[video] Hagia Sophia", Case.Sensitive, "a liked topic is still done");

        var english = ConceptPromptBuilder.BuildTopics(new TopicPromptInput(Profile(ContentLanguages.English), "en", string.Empty, memory, [], 10));
        english.User.ShouldContain("DISLIKED VIDEOS (disliked — stay away from these and close topics; title: summary):\n- Iconoclasm");
        english.User.ShouldContain("LIKED VIDEOS (liked — similar spirit, but different topics; title: summary):\n- Hagia Sophia: Two sentences. About it.");
        english.System.ShouldContain("The DISLIKED VIDEOS are the strongest warning");

        var turkish = ConceptPromptBuilder.BuildTopics(new TopicPromptInput(Profile(ContentLanguages.Turkish), "tr", string.Empty, memory, [], 10));
        turkish.User.ShouldContain("BEĞENİLMEYEN VİDEOLAR (beğenilmedi — bu ve yakın konulardan uzak dur; başlık: özet):\n- Iconoclasm");
        turkish.User.ShouldContain("BEĞENİLEN VİDEOLAR (beğenilenler — benzer ruhta ama farklı konular; başlık: özet):\n- Hagia Sophia");
        turkish.System.ShouldContain("BEĞENİLMEYEN VİDEOLAR en güçlü uyarıdır");

        foreach (var language in new[] { ContentLanguages.English, ContentLanguages.Turkish })
        {
            var concepts = ConceptPromptBuilder.Build(new ConceptPromptInput(Profile(language), [], memory, string.Empty, 5));
            concepts.User.ShouldContain(language == ContentLanguages.English ? "DISLIKED VIDEOS" : "BEĞENİLMEYEN VİDEOLAR");
            concepts.User.ShouldContain(language == ContentLanguages.English ? "LIKED VIDEOS" : "BEĞENİLEN VİDEOLAR");
            concepts.System.ShouldContain(language == ContentLanguages.English ? "The LIKED VIDEOS are good examples" : "BEĞENİLEN VİDEOLAR iyi örnektir");
        }
    }

    [Fact]
    public void ProducingAVideoIsNoLike()
    {
        var memory = PromptMemoryBuilder.Build([Of(1, "Hagia Sophia"), Of(2, "Greek fire")], [], ContentLanguages.English);

        memory.LikedVideos.ShouldBeNull("producing a video does not rate it");
        memory.DislikedVideos.ShouldBeNull();
        memory.Covered.ShouldNotBeNull().ShouldContain("[video] Hagia Sophia", Case.Sensitive, "only covered, neutral");
        var prompt = ConceptPromptBuilder.BuildTopics(new TopicPromptInput(Profile(ContentLanguages.English), "en", string.Empty, memory, [], 10));
        prompt.User.ShouldNotContain("LIKED VIDEOS");
        prompt.User.ShouldNotContain("DISLIKED VIDEOS");
    }

    [Fact]
    public void OnlyTheNewestRatingsOfEachKindAreSentNewestFirst()
    {
        var ratings = Enumerable.Range(1, PromptMemoryLimits.RatingsEach + 3).Select(i => Rated("Liked " + i, VideoVote.Like, i))
            .Append(Rated("Old dislike", VideoVote.Dislike, 0)).ToList();

        var memory = PromptMemoryBuilder.Build([], [], ContentLanguages.English, null, ratings);

        var lines = memory.LikedVideos.ShouldNotBeNull().Split("\n- ", StringSplitOptions.RemoveEmptyEntries);
        lines.Length.ShouldBe(PromptMemoryLimits.RatingsEach);
        lines[0].ShouldStartWith("Liked " + (PromptMemoryLimits.RatingsEach + 3) + ":");
        memory.DislikedVideos.ShouldBe("\n- Old dislike: Two sentences. About it.");
    }

    [Fact]
    public void AVideoIsFoundByItsConceptElseByItsFolderWithoutTheVersion()
    {
        var concept = Guid.NewGuid();
        var byConcept = Rated("A", VideoVote.Like, 1, concept);
        var byFolder = new VideoRating { Title = "B", VideoGroup = "Channel/Greek fire", Vote = VideoVote.Dislike };

        VideoRater.GroupOf("Channel/Greek fire/v2").ShouldBe("Channel/Greek fire");
        VideoRater.GroupOf(Path.Combine("Channel", "Greek fire", "v1")).ShouldBe("Channel/Greek fire");
        VideoRater.GroupOf("Channel/Greek fire").ShouldBe("Channel/Greek fire", "a folder that is no version folder is its own");
        VideoRater.Find([byConcept, byFolder], new VideoRecord { ConceptId = concept, Folder = "Channel/Other/v3" }).ShouldBe(byConcept);
        VideoRater.Find([byConcept, byFolder], new VideoRecord { Folder = "Channel/Greek fire/v3" }).ShouldBe(byFolder, "the concept was deleted");
        VideoRater.Find([byConcept, byFolder], new VideoRecord { Folder = "Channel/Basil/v1" }).ShouldBeNull();
    }
}
