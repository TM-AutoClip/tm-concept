using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Concepts;
using TMConcept.Core.DiskSpace;
using TMConcept.Core.Documents;
using TMConcept.Core.Profiles;
using TMConcept.Documents.Extractors;
using TMConcept.Storage.Backup;
using TMConcept.Storage.Crypto;
using TMConcept.Storage.Database;
using TMConcept.Storage.Stores;
using TMConcept.Tests.Core;

namespace TMConcept.Tests.Storage;

/// <summary>
/// 0.1.21: the documents, their passages and vectors in the encrypted database (Version0120), the library's copies, deleting, the upgrade
/// of a 0.1.20 file with data and the backup (records, passages and copies go with the profiles).
/// </summary>
public sealed class DocumentStorageTests
{
    private const string Password = "backup password 123";

    private static readonly CancellationToken None = CancellationToken.None;

    private static DocumentLibrary Library(StorageHarness h, IDocumentEmbedder? embedder = null) =>
        new(h.Paths, new DocumentStore(h.Database), [new PlainTextExtractor(), new MarkdownExtractor()], [], embedder ?? new FixedEmbedder(false),
            TimeProvider.System, NullLogger<DocumentLibrary>.Instance);

    private static async Task<(Profile Profile, string File)> PrepareAsync(StorageHarness h, string text = "Silk came by caravan.\n\nThe emperor taxed it.")
    {
        var profile = new Profile { Name = "Byzantium", ContentLanguage = ContentLanguages.English };
        await new ProfileStore(h.Database).SaveAsync(profile, None);
        var file = Path.Combine(h.Paths.DataDirectory, "incoming", "trade.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, text, None);
        return (profile, file);
    }

    [Fact]
    public async Task AFileIsCopiedReadCutAndStoredOnceAndItsVectorsAreMadeWhenTheModelIsThere()
    {
        using var h = await StorageHarness.CreatedAsync();
        var (profile, file) = await PrepareAsync(h);
        var library = Library(h, new FixedEmbedder(true));
        var stages = new List<DocumentStage>();

        var added = await library.AddFileAsync(profile.Id, file, new SyncProgress<DocumentProgress>(p => stages.Add(p.Stage)), None);

        var document = added.Document;
        added.EmbeddingError.ShouldBeEmpty();
        (document.Kind, document.Title, document.OriginalName, document.Status, document.Chunks).ShouldBe((DocumentKind.Text, "trade", "trade.txt", DocumentStatus.Ready, 1));
        var copy = library.FileOf(document);
        copy.ShouldStartWith(Path.Combine(h.Paths.DataDirectory, "documents", profile.Id.ToString("N")));
        (await File.ReadAllTextAsync(copy, None)).ShouldBe(await File.ReadAllTextAsync(file, None), "the app keeps its own, unencrypted copy");
        stages.ShouldBe([DocumentStage.Reading, DocumentStage.Extracting, DocumentStage.Chunking, DocumentStage.Embedding, DocumentStage.Embedding, DocumentStage.Done]);
        var store = new DocumentStore(h.Database);
        (await store.ChunksAsync(profile.Id, [document.Id], None)).ShouldHaveSingleItem().Text.ShouldContain("emperor");
        (await store.VectorsAsync(profile.Id, "fixed", None)).ShouldHaveSingleItem().Dimensions.ShouldBe(2);
        (await store.ChunksWithoutVectorAsync("fixed", None)).ShouldBeEmpty();

        var again = await Should.ThrowAsync<DocumentRejectedException>(() => library.AddFileAsync(profile.Id, file, null, None));
        (again.Code, again.Detail).ShouldBe(("duplicate", "trade"));
        var odd = Path.Combine(h.Paths.DataDirectory, "incoming", "slides.pptx");
        await File.WriteAllTextAsync(odd, "x", None);
        (await Should.ThrowAsync<DocumentRejectedException>(() => library.AddFileAsync(profile.Id, odd, null, None))).Code.ShouldBe("type");
    }

    [Fact]
    public async Task AFileWithoutTextIsKeptAsNoTextAndAnUnreadableOneLeavesNothingBehind()
    {
        using var h = await StorageHarness.CreatedAsync();
        var (profile, file) = await PrepareAsync(h, "   \n\n  ");
        var library = new DocumentLibrary(h.Paths, new DocumentStore(h.Database), [new PlainTextExtractor(), new BrokenExtractor()], [], new FixedEmbedder(false),
            TimeProvider.System, NullLogger<DocumentLibrary>.Instance);

        var empty = await library.AddFileAsync(profile.Id, file, null, None);
        (empty.Document.Status, empty.Document.StatusDetail, empty.Document.Chunks).ShouldBe((DocumentStatus.NoText, "no_text", 0));

        var broken = Path.Combine(h.Paths.DataDirectory, "incoming", "broken.md");
        await File.WriteAllTextAsync(broken, "# x", None);
        (await Should.ThrowAsync<DocumentRejectedException>(() => library.AddFileAsync(profile.Id, broken, null, None))).Code.ShouldBe("unreadable");
        Directory.GetFiles(Path.Combine(h.Paths.DataDirectory, "documents", profile.Id.ToString("N"))).ShouldHaveSingleItem("only the first document's copy stays");
        (await library.ListAsync(profile.Id, None)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task DeletingADocumentDiscardsItsCopyFirstAndThenItsPassagesAndVectors()
    {
        using var h = await StorageHarness.CreatedAsync();
        var (profile, file) = await PrepareAsync(h);
        var library = Library(h, new FixedEmbedder(true));
        var document = (await library.AddFileAsync(profile.Id, file, null, None)).Document;
        var copy = library.FileOf(document);
        var bin = new FakeDiscard { Fail = true };

        await Should.ThrowAsync<IOException>(() => library.DeleteAsync(document, bin, None));
        (await library.ListAsync(profile.Id, None)).ShouldHaveSingleItem("a copy that could not be discarded keeps its document");

        bin.Fail = false;
        await library.DeleteAsync(document, bin, None);

        bin.Discarded.ShouldBe([copy]);
        File.Exists(copy).ShouldBeFalse();
        (await library.ListAsync(profile.Id, None)).ShouldBeEmpty();
        (await h.QueryAsync(db => db.DocumentChunks.CountAsync())).ShouldBe(0);
        (await h.QueryAsync(db => db.DocumentChunkVectors.CountAsync())).ShouldBe(0);
    }

    [Fact]
    public async Task DeletingTheProfileDeletesItsDocumentsWithTheirPassages()
    {
        using var h = await StorageHarness.CreatedAsync();
        var (profile, file) = await PrepareAsync(h);
        await Library(h, new FixedEmbedder(true)).AddFileAsync(profile.Id, file, null, None);

        await new ProfileStore(h.Database).DeleteAsync(profile.Id, None);

        (await h.QueryAsync(db => db.Documents.CountAsync())).ShouldBe(0);
        (await h.QueryAsync(db => db.DocumentChunks.CountAsync())).ShouldBe(0);
        (await h.QueryAsync(db => db.DocumentChunkVectors.CountAsync())).ShouldBe(0);
    }

    [Fact]
    public async Task AFileOf0120WithDataIsUpgradedToDocuments()
    {
        using var h = await StorageHarness.CreatedAsync();
        var profileId = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
        var concept = new Concept { ProfileId = profileId, Title = "Mohács", Summary = "s", Content = "c", Language = ContentLanguages.Turkish, CreatedAtUtc = at };

        // The file as 0.1.20 left it: its three steps, then a profile and a concept as 0.1.20 wrote them.
        var steps = await h.QueryAsync(async db =>
        {
            var all = db.Database.GetMigrations().ToList();
            all.Select(SchemaUpgrader.Name).ShouldBe(["Version0117", "Version0118", "Version0119", "Version0120"]);
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("0", None);
            await migrator.MigrateAsync(all[2], None);
            return all;
        });
        await using (var old = Version0118Context.Open(h.ConnectionString))
        {
            old.Profiles.Add(new OldProfileRow
            {
                Id = profileId.ToString("D").ToUpperInvariant(), Name = "Old channel", ContentLanguage = ContentLanguages.Turkish, TargetSeconds = 90,
                ConceptText = "Ottoman history.", ConceptCounter = 1, CreatedAtUtc = at.UtcTicks, UpdatedAtUtc = at.UtcTicks,
            });
            await old.SaveChangesAsync(None);
            old.Concepts.Add(OldConceptRow.Of(concept, 1));
            await old.SaveChangesAsync(None);
        }

        h.Vault.Lock();
        await h.Vault.OpenAsync(None);

        (await h.QueryAsync(async db => (await db.Database.GetAppliedMigrationsAsync(None)).ToList())).ShouldBe(steps);
        var back = (await new ConceptStore(h.Database).GetAsync(concept.Id, None)).ShouldNotBeNull();
        (back.Title, back.Sources).ShouldBe(("Mohács", string.Empty), "an older concept has no sources");
        var store = new DocumentStore(h.Database);
        (await store.ListAsync(profileId, None)).ShouldBeEmpty("no document is made by the upgrade");
        var document = new SourceDocument { ProfileId = profileId, Title = "Trade", Sha256 = "abc", Chunks = 1 };
        await store.AddAsync(document, [new DocumentChunk { DocumentId = document.Id, ProfileId = profileId, Text = "Silk." }], None);
        (await store.ChunksAsync(profileId, [document.Id], None)).ShouldHaveSingleItem().Text.ShouldBe("Silk.");
        concept.Sources = "Trade";
    }

    [Fact]
    public async Task DocumentsGoWithTheProfilesIntoABackupWithTheirCopiesAndComeBack()
    {
        using var source = await StorageHarness.CreatedAsync();
        var (profile, file) = await PrepareAsync(source);
        var library = Library(source, new FixedEmbedder(true));
        var document = (await library.AddFileAsync(profile.Id, file, null, None)).Document;
        var service = Service(source);
        await service.SetPasswordAsync(Password, None);
        var all = Path.Combine(source.Paths.DataDirectory, "all" + BackupRules.Extension);
        var noProfiles = Path.Combine(source.Paths.DataDirectory, "no-profiles" + BackupRules.Extension);
        await service.CreateAsync(all, BackupContents.Automatic, None);
        await service.CreateAsync(noProfiles, new BackupContents { Profiles = false, Concepts = false }, None);

        using (var target = new StorageHarness())
        {
            await Service(target).RestoreAsync(all, Password, target.Protector, None);
            var store = new DocumentStore(target.Database);
            (await store.ListAsync(profile.Id, None)).ShouldHaveSingleItem().Id.ShouldBe(document.Id);
            (await store.ChunksAsync(profile.Id, [document.Id], None)).ShouldHaveSingleItem();
            (await store.VectorsAsync(profile.Id, "fixed", None)).ShouldHaveSingleItem();
            var copy = Library(target).FileOf(document);
            (await File.ReadAllTextAsync(copy, None)).ShouldBe(await File.ReadAllTextAsync(file, None), "the copy is a file of the backup");
        }

        using (var target = new StorageHarness())
        {
            await Service(target).RestoreAsync(noProfiles, Password, target.Protector, None);
            (await target.QueryAsync(db => db.Documents.CountAsync())).ShouldBe(0);
            (await target.QueryAsync(db => db.DocumentChunks.CountAsync())).ShouldBe(0);
            Directory.Exists(Path.Combine(target.Paths.DataDirectory, "documents")).ShouldBeFalse();
        }
    }

    private static BackupService Service(StorageHarness h) =>
        new(h.Paths, h.Key, h.Vault, h.Database, KdfParameters.Fast, TimeProvider.System, NullLogger<BackupService>.Instance);

    /// <summary>Reports at once (a Progress would post later).</summary>
    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    /// <summary>A meaning model with fixed 2-dimensional vectors (or not installed).</summary>
    private sealed class FixedEmbedder(bool ready) : DocumentEmbedderBase
    {
        public override string ModelId => "fixed";

        public override string ComponentCode => "embedding.fixed";

        public override bool Ready => ready;

        protected override Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => new[] { 0.6f, 0.8f }).ToList());
    }

    /// <summary>A Markdown reader that finds every file damaged.</summary>
    private sealed class BrokenExtractor : DocumentExtractorBase
    {
        public override DocumentKind Kind => DocumentKind.Markdown;

        public override IReadOnlyList<string> Extensions { get; } = [".md"];

        protected override ExtractedDocument Read(string file, CancellationToken cancellationToken) => throw new InvalidDataException("damaged");
    }

    /// <summary>A recycle bin that deletes what it is given, or fails.</summary>
    private sealed class FakeDiscard : IFolderDiscard
    {
        public List<string> Discarded { get; } = [];

        public bool Fail { get; set; }

        public bool Permanent => false;

        public Task DiscardAsync(string path, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new IOException("no recycle bin");
            }

            Discarded.Add(path);
            File.Delete(path);
            return Task.CompletedTask;
        }
    }
}
