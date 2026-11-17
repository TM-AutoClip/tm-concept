using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Components;
using TMConcept.Core.Documents;
using TMConcept.Core.Tools;
using TMConcept.Documents.Embeddings;
using TMConcept.Tests.Storage;

namespace TMConcept.Tests.Documents;

/// <summary>The documents' meaning model: its prefixes, its pooling, its pinned files, its component, and that ONNX Runtime loads in this process.</summary>
public sealed class EmbedderTests
{
    [Fact]
    public async Task PassagesAndQueriesCarryTheirPrefix()
    {
        var embedder = new RecordingEmbedder();
        await embedder.EmbedPassagesAsync(["  Silk came by caravan. "], CancellationToken.None);
        await embedder.EmbedQueryAsync("silk", CancellationToken.None);
        embedder.Seen.ShouldBe(["passage: Silk came by caravan.", "query: silk"]);
        await Should.ThrowAsync<ArgumentException>(() => embedder.EmbedQueryAsync(" ", CancellationToken.None));
    }

    [Fact]
    public async Task AModelThatIsNotInstalledSaysSoBeforeAnythingRuns()
    {
        var embedder = new RecordingEmbedder { Installed = false };
        await Should.ThrowAsync<InvalidOperationException>(() => embedder.EmbedQueryAsync("silk", CancellationToken.None));
        embedder.Seen.ShouldBeEmpty();
    }

    [Fact]
    public void PoolingAveragesTheTokensAndNormalizes()
    {
        var vector = OnnxTextEncoder.Pool([1, 0, 3, 0], [1, 2, 2]);
        vector.ShouldBe([1f, 0f]);
        var other = OnnxTextEncoder.Pool([3, 4], [1, 2]);
        other[0].ShouldBe(0.6f, 0.0001f);
        other[1].ShouldBe(0.8f, 0.0001f);
    }

    [Fact]
    public void TheModelFilesArePinnedByCommitAndChecksum()
    {
        E5Model.Files.Count.ShouldBe(2);
        E5Model.Files.ShouldAllBe(f => f.Url.Host == "huggingface.co" && f.Url.AbsolutePath.Contains("/resolve/614241f622f53c4eeff9890bdc4f31cfecc418b3/")
            && f.Sha256.Length == 64);
        E5Model.Files.Sum(f => f.ApproximateBytes).ShouldBe(118_346_824 + 17_082_730);
    }

    [Fact]
    public void TheComponentListsItsDownloadsUntilTheFilesAreThere()
    {
        using var paths = new TempPaths();
        var embedder = new E5Embedder(paths, new RefusingDownloader(), NullLogger<E5Embedder>.Instance);
        var component = new E5ComponentSource(embedder).Components.ShouldHaveSingleItem();

        component.Code.ShouldBe(ComponentCodes.EmbeddingPrefix + "multilingual-e5-small");
        component.Ready.ShouldBeFalse();
        component.Downloads().Select(d => d.Host).ShouldBe(["huggingface.co", "huggingface.co"]);
        component.Folders().ShouldBe([embedder.Folder]);
        ((IDocumentEmbedder)embedder).ComponentCode.ShouldBe(component.Code);

        Directory.CreateDirectory(embedder.Folder);
        File.WriteAllText(Path.Combine(embedder.Folder, E5Model.ModelFile), "x");
        File.WriteAllText(Path.Combine(embedder.Folder, E5Model.TokenizerFile), "x");
        component.Ready.ShouldBeTrue();
        component.Downloads().ShouldBeEmpty();
    }

    [Fact]
    public void OnnxRuntimeLoadsInThisProcess() => OnnxTextEncoder.RuntimeVersion.ShouldStartWith("1.");

    /// <summary>A meaning model that records what it is given and returns a unit vector.</summary>
    private sealed class RecordingEmbedder : DocumentEmbedderBase
    {
        public List<string> Seen { get; } = [];

        public bool Installed { get; init; } = true;

        public override string ModelId => "test";

        public override string ComponentCode => "embedding.test";

        public override bool Ready => Installed;

        protected override Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
        {
            Seen.AddRange(texts);
            return Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => new[] { 1f, 0f }).ToList());
        }
    }

    /// <summary>A downloader that must never be called.</summary>
    private sealed class RefusingDownloader : IArchiveDownloader
    {
        public Task DownloadAsync(ToolArchive archive, string file, IProgress<ToolProgress>? progress, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Nothing is downloaded in this test.");
    }
}
