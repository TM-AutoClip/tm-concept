using Shouldly;
using TMConcept.Core.Videos;

namespace TMConcept.Tests.Core;

/// <summary>
/// The version's asset record (0.1.44, F15) and the attribution note (H2): <c>assets.json</c> is written and read back, and only an entry that
/// asks for credit (CC BY and its variants, or an attribution the user wrote) gives the note's text.
/// </summary>
public sealed class AssetLedgerTests : IDisposable
{
    private readonly string _folder = Path.Combine(TestFolders.Root, "tmc-assets-" + Guid.NewGuid().ToString("N"));

    public AssetLedgerTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public async Task TheRecordIsWrittenNextToTheVideoAndReadBack()
    {
        var entry = new AssetEntry
        {
            Kind = AssetKinds.Music,
            Name = "Gymnopédie",
            Artist = "Kevin MacLeod",
            SourceUrl = "https://incompetech.com",
            License = "CC BY 4.0",
            Attribution = string.Empty,
            File = "gymnopedie.mp3",
        };

        await AssetLedger.WriteAsync(_folder, [entry], CancellationToken.None);

        var file = Path.Combine(_folder, VideoFolders.AssetsFile);
        File.Exists(file).ShouldBeTrue();
        var json = await File.ReadAllTextAsync(file, CancellationToken.None);
        json.ShouldContain("\"license\": \"CC BY 4.0\"", customMessage: "readable, camelCase, indented");
        json.ShouldContain("Gymnopédie", customMessage: "not escaped");
        var read = await AssetLedger.ReadAsync(_folder, CancellationToken.None);
        read.Count.ShouldBe(1);
        read[0].Name.ShouldBe("Gymnopédie");
        read[0].Kind.ShouldBe(AssetKinds.Music);
        read[0].File.ShouldBe("gymnopedie.mp3");
    }

    [Fact]
    public async Task AVersionWithoutOutsideFilesHasAnEmptyRecordAndAnOldOneNone()
    {
        await AssetLedger.WriteAsync(_folder, [], CancellationToken.None);
        (await File.ReadAllTextAsync(Path.Combine(_folder, VideoFolders.AssetsFile), CancellationToken.None)).Trim().ShouldBe("[]");
        (await AssetLedger.ReadAsync(_folder, CancellationToken.None)).ShouldBeEmpty();

        var old = Path.Combine(_folder, "old");
        Directory.CreateDirectory(old);
        (await AssetLedger.ReadAsync(old, CancellationToken.None)).ShouldBeEmpty("made before 0.1.44");

        await File.WriteAllTextAsync(Path.Combine(old, VideoFolders.AssetsFile), "{ not json", CancellationToken.None);
        (await AssetLedger.ReadAsync(old, CancellationToken.None)).ShouldBeEmpty("a damaged record shows no note");
    }

    [Theory]
    [InlineData("CC BY 4.0", "", true)]
    [InlineData("CC-BY-SA-3.0", "", true)]
    [InlineData("cc by-nc 4.0", "", true)]
    [InlineData("CC0 1.0", "", false)]
    [InlineData("Pixabay Content License", "", false)]
    [InlineData(AssetLicenses.OwnFile, "", false)]
    [InlineData(AssetLicenses.OwnFile, "Music: \"Song\" by Someone", true)]
    public void OnlyAnEntryThatAsksForCreditNeedsAttribution(string license, string attribution, bool needs) =>
        AssetLedger.NeedsAttribution(new AssetEntry { Name = "x", License = license, Attribution = attribution }).ShouldBe(needs);

    [Fact]
    public void TheNoteTextHasOnlyTheEntriesThatAskForCredit()
    {
        AssetEntry[] entries =
        [
            new() { Name = "Own", License = AssetLicenses.OwnFile },
            new() { Name = "Free", License = "CC0 1.0" },
            new() { Name = "Song", Artist = "Kevin MacLeod", License = "CC BY 4.0", SourceUrl = "https://incompetech.com" },
            new() { Name = "Mine", License = AssetLicenses.OwnFile, Attribution = "  Music: Mine by Me (CC BY 4.0)  " },
        ];

        var text = AssetLedger.AttributionText(entries);

        text.ShouldBe("\"Song\" — Kevin MacLeod · CC BY 4.0 · https://incompetech.com" + Environment.NewLine + "Music: Mine by Me (CC BY 4.0)");
        AssetLedger.AttributionText(entries.Take(2)).ShouldBeEmpty("no entry asks for credit: no note");
    }
}
