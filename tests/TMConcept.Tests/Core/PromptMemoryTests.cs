using Shouldly;
using TMConcept.Core.Concepts;
using TMConcept.Core.Profiles;

namespace TMConcept.Tests.Core;

/// <summary>The profile's past as the prompts send it: capped, condensed, dislikes and wishes apart (10 Nov 2026).</summary>
public sealed class PromptMemoryTests
{
    private static Concept Of(int number, string title, ConceptStatus status = ConceptStatus.New) =>
        new() { Number = number, Title = title, Summary = "s", Status = status };

    [Fact]
    public void AThousandConceptsStayUnderTheCapWithTheNewestFortyListedAndTheRightTotal()
    {
        var concepts = Enumerable.Range(1, 1000).Select(i => (i % 5) switch
        {
            0 => Of(i, $"Nika Riot — Part {i / 5}"),
            1 => Of(i, $"The Vikings in Miklagard, saga {i}"),
            2 => Of(i, $"Hagia Sophia, year {i}", i > 990 ? ConceptStatus.Produced : ConceptStatus.Approved),
            3 => Of(i, $"Walls of Theodosius, gate {i}"),
            _ => Of(i, $"Emperor number {i} and his court"),
        }).ToList();

        var memory = PromptMemoryBuilder.Build(concepts, [], ContentLanguages.English);

        memory.CoveredCount.ShouldBe(1000);
        memory.Covered!.Length.ShouldBeLessThanOrEqualTo(PromptMemoryLimits.CoveredMaxCharacters + 3);
        var lines = memory.Covered.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Length.ShouldBeLessThanOrEqualTo(PromptMemoryLimits.CoveredNewest + PromptMemoryLimits.CoveredThemeLines);
        lines[0].ShouldBe("- Nika Riot — Part 200", "newest first");
        lines.ShouldContain("- [video] Hagia Sophia, year 997", "a concept made into a video is marked");
        lines.Take(PromptMemoryLimits.CoveredNewest).Select(l => l[2..].Replace("[video] ", string.Empty, StringComparison.Ordinal))
            .ShouldBe(concepts.OrderByDescending(c => c.Number).Take(PromptMemoryLimits.CoveredNewest).Select(c => c.Title));
        var older = lines.Skip(PromptMemoryLimits.CoveredNewest).ToList();
        older.ShouldContain(l => l.StartsWith("- Nika Riot — Part 1–", StringComparison.Ordinal), "a series is one line");
        older.ShouldContain(l => l.Contains("×", StringComparison.Ordinal) && l.Contains("Vikings", StringComparison.Ordinal), "a theme with its count");
        older.ShouldContain(l => l.StartsWith("- Sophia ×", StringComparison.Ordinal));
        older.Count.ShouldBeLessThanOrEqualTo(PromptMemoryLimits.CoveredThemeLines);
    }

    [Fact]
    public void ThemesCountEveryTitleThatSharesTheWordWhereverItStands()
    {
        var older = new[] { "Vikings at the walls", "How the Vikings traded", "1500 years of Hagia Sophia", "The mosaics of Hagia Sophia", "Greek fire" };

        var lines = PromptMemoryBuilder.Condense(older, OutputLanguages.Of(ContentLanguages.English));

        lines.ShouldContain("Vikings ×2: Vikings at the walls; How the Vikings traded");
        lines.ShouldContain("Sophia ×2: 1500 years of Hagia Sophia; The mosaics of Hagia Sophia");
        lines.ShouldContain("Greek fire", "a single title stays as it is");
    }

    [Fact]
    public void AVeryLongHistoryIsCutFromTheOlderLinesFirst()
    {
        var concepts = Enumerable.Range(1, 3000).Select(i => Of(i, new string((char)('a' + (i % 26)), 85) + i)).ToList();

        var memory = PromptMemoryBuilder.Build(concepts, [], ContentLanguages.English);

        memory.Covered!.Length.ShouldBeLessThanOrEqualTo(PromptMemoryLimits.CoveredMaxCharacters + 3);
        memory.CoveredCount.ShouldBe(3000, "the heading still tells how many there are");
        memory.Covered.ShouldStartWith("\n- " + concepts[^1].Title + "\n", Case.Sensitive, "the newest stay");
    }

    [Fact]
    public void OnlyTheNewestDislikesAndTrimmedWishesGo()
    {
        var at = DateTimeOffset.UtcNow;
        var concepts = Enumerable.Range(1, 15).Select(i => new Concept
        {
            Number = i,
            Title = "Topic " + i,
            Summary = new string('x', 400),
            Status = ConceptStatus.Trashed,
            TrashReason = i % 2 == 0 ? TrashReason.Topic : TrashReason.Telling,
            DecidedAtUtc = at.AddMinutes(i),
        }).ToList();
        var wishes = Enumerable.Range(1, 8).Select(i => "Wish " + i + " " + new string('w', 300)).ToList();

        var memory = PromptMemoryBuilder.Build(concepts, wishes, ContentLanguages.English);

        memory.DislikedTopics!.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length.ShouldBe(7);
        memory.DislikedTelling!.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length.ShouldBe(8);
        memory.DislikedTelling.ShouldStartWith("\n- Topic 15: ");
        memory.DislikedTelling.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .ShouldAllBe(l => l.Length <= 2 + ConceptLimits.TitleMax + 2 + PromptMemoryLimits.DislikeSummaryMax);
        var sent = memory.Wishes!.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        sent.Length.ShouldBe(8);
        sent[0].ShouldStartWith("- «Wish 1 ");
        sent.ShouldAllBe(l => l.Length <= PromptMemoryLimits.WishMax + 4);
        memory.CoveredCount.ShouldBe(8, "the telling dislikes are covered, the listed topic dislikes are not");

        var many = Enumerable.Range(1, 30).Select(i => new Concept { Number = i, Title = "T" + i, Status = ConceptStatus.Trashed, TrashReason = TrashReason.Topic,
            DecidedAtUtc = at.AddMinutes(i) }).ToList();
        var capped = PromptMemoryBuilder.Build(many, [], ContentLanguages.English);
        capped.DislikedTopics!.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length.ShouldBe(PromptMemoryLimits.DislikesEach);
        capped.DislikedTopics.ShouldStartWith("\n- T30\n");
        capped.CoveredCount.ShouldBe(20, "older topic dislikes are still covered");
    }

    [Fact]
    public void LongTitlesLeaveRoomForTheCondensedOlderHistory()
    {
        string[] themes = ["Vikings", "Hagia Sophia", "Theodosian walls", "Greek fire", "Nika riot", "Iconoclasm"];
        var concepts = Enumerable.Range(1, 1000)
            .Select(i => Of(i, (themes[i % themes.Length] + $" and the long untold story number {i} of the city by the sea ").PadRight(80, 'x')[..80],
                i > 980 ? ConceptStatus.Produced : ConceptStatus.New))
            .ToList();

        var memory = PromptMemoryBuilder.Build(concepts, [], ContentLanguages.English);

        memory.Covered!.Length.ShouldBeLessThanOrEqualTo(PromptMemoryLimits.CoveredMaxCharacters + 3);
        memory.CoveredCount.ShouldBe(1000);
        var lines = memory.Covered.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines[0].ShouldContain(concepts[^1].Title, Case.Sensitive, "the newest stays first");
        var themed = lines.Where(l => l.Contains('×', StringComparison.Ordinal)).ToList();
        themed.Count.ShouldBeGreaterThanOrEqualTo(4, "80-character titles must not push every condensed line out");
        themed.ShouldContain(l => l.Contains("Vikings", StringComparison.Ordinal));
        themed.ShouldContain(l => l.Contains("Iconoclasm", StringComparison.Ordinal));
    }

    [Fact]
    public void TheChannelsOwnSubjectNamesNoThemeSoTheLinesNameItsSubThemes()
    {
        string[] subjects = ["Mohács", "Çaldıran", "Kanuni", "Lale Devri", "Yeniçeri", "Tersane"];
        var older = Enumerable.Range(1, 60).Select(i => $"Osmanlı ve {subjects[i % subjects.Length]}: olay {i}").ToList();

        foreach (var content in new[] { "Osmanlı tarihi", null })
        {
            var lines = PromptMemoryBuilder.Condense(older, OutputLanguages.Of(ContentLanguages.Turkish), content);

            lines.ShouldNotContain(l => l.StartsWith("Osmanlı ×", StringComparison.Ordinal), content ?? "a word most titles share");
            lines.ShouldContain(l => l.StartsWith("Mohács ×10", StringComparison.Ordinal), content ?? "a word most titles share");
            lines.ShouldContain(l => l.StartsWith("Yeniçeri ×10", StringComparison.Ordinal), content ?? "a word most titles share");
        }
    }

    [Fact]
    public void ATellingDislikeSendsItsOpeningSentenceNotItsTopic()
    {
        var disliked = new Concept
        {
            Number = 1,
            Title = "The Nika riot",
            Summary = "The riot of 532. What the viewer learns.",
            Content = "[on screen: map of the Hippodrome]\n[pause 1 s] Here are ten dates you must know. First, 532.\n[on screen: crowd]\nMore.",
            Status = ConceptStatus.Trashed,
            TrashReason = TrashReason.Telling,
        };

        var memory = PromptMemoryBuilder.Build([disliked], [], ContentLanguages.English);

        memory.DislikedTelling.ShouldBe("\n- The Nika riot: Here are ten dates you must know.");
    }

    [Fact]
    public void AnEmptyProfileHasNoPast()
    {
        PromptMemoryBuilder.Build([], [], ContentLanguages.Turkish).ShouldBe(PromptMemory.Empty);
    }
}
