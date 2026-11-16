using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Endpoints;
using TMConcept.Core.Models;

namespace TMConcept.Tests.Core;

/// <summary>Every catalog entry has one model: online, a local server's model on this computer (on the card), or a local engine's.</summary>
public sealed class ModelDirectoryTests
{
    [Theory]
    [InlineData("http://localhost:11434/v1", true)]
    [InlineData("http://127.0.0.1:1234/v1", true)]
    [InlineData("http://[::1]:8080/v1", true)]
    [InlineData("http://lemonade.localhost:13305/api/v1", true)]
    [InlineData("http://192.168.1.20:11434/v1", false)]
    [InlineData("https://gpu-box.example.org/v1", false)]
    [InlineData("not an address", false)]
    public void AServerOnThisComputerIsLocal(string address, bool local) => EndpointModelSource.OnThisComputer(address).ShouldBe(local);

    [Fact]
    public async Task ServerModelsAreLocalOnlyOnThisComputerAndOnlineModelsNeedAKey()
    {
        var registry = new EndpointRegistry(new MemoryEndpoints(), NullLogger<EndpointRegistry>.Instance);
        var here = new LocalEndpoint { Name = "Ollama", BaseUrl = "http://localhost:11434/v1", Models = [new EndpointModel { Id = "qwen3:8b" }] };
        var there = new LocalEndpoint { Name = "Box", BaseUrl = "http://192.168.1.20:11434/v1", Models = [new EndpointModel { Id = "qwen3:8b" }] };
        await registry.SaveAsync(here, CancellationToken.None);
        await registry.SaveAsync(there, CancellationToken.None);
        var control = new RecordingControl();
        var directory = new ModelDirectory([new EndpointModelSource(registry, [control], NullLogger<EndpointModelSource>.Instance)]);

        var local = directory.Get(EndpointCodes.Catalog(here)[0]).ShouldBeAssignableTo<ILocalModel>()!;
        local.Device.ShouldBe(ModelDevice.Gpu);
        local.IsLocal.ShouldBeTrue();
        directory.Get(EndpointCodes.Catalog(here)[0]).ShouldBeSameAs(local, "one object per model for the run");
        await local.UnloadAsync(CancellationToken.None);
        control.Unloaded.ShouldBe(["Ollama qwen3:8b"]);

        var remote = directory.Get(EndpointCodes.Catalog(there)[0]).ShouldBeOfType<OnlineModel>();
        remote.NeedsKey.ShouldBeFalse("a server needs no API key");
        directory.Get(new CatalogModel { Provider = "openai", Id = "gpt-5.5" }).ShouldBeOfType<OnlineModel>().NeedsKey.ShouldBeTrue();
    }

    [Fact]
    public async Task AnUnloadThatIsNotConfirmedSaysSo()
    {
        var registry = new EndpointRegistry(new MemoryEndpoints(), NullLogger<EndpointRegistry>.Instance);
        var ollama = new LocalEndpoint { Name = "Ollama", Kind = EndpointKind.Ollama, BaseUrl = "http://localhost:11434/v1", Models = [new EndpointModel { Id = "qwen3:8b" }] };
        var plain = new LocalEndpoint { Name = "llama-server", BaseUrl = "http://localhost:8080/v1", Models = [new EndpointModel { Id = "gemma" }] };
        await registry.SaveAsync(ollama, CancellationToken.None);
        await registry.SaveAsync(plain, CancellationToken.None);
        var control = new RecordingControl { Answer = EndpointUnload.Failed };
        var directory = new ModelDirectory([new EndpointModelSource(registry, [new NotMineControl(), control], NullLogger<EndpointModelSource>.Instance)], registry);

        var held = (ILocalModel)directory.Get(EndpointCodes.Catalog(ollama)[0]);
        await held.LoadAsync(CancellationToken.None);
        await Should.ThrowAsync<ModelUnloadException>(() => held.UnloadAsync(CancellationToken.None));
        held.IsLoaded.ShouldBeTrue("the server still holds it");

        // A plain server no control knows cannot be asked: not "unloaded" either.
        control.Answer = EndpointUnload.NotMine;
        var other = (ILocalModel)directory.Get(EndpointCodes.Catalog(plain)[0]);
        await Should.ThrowAsync<ModelUnloadException>(() => other.UnloadAsync(CancellationToken.None));

        // Deleted while loaded: the unload still goes to the server it ran on.
        control.Answer = EndpointUnload.Unloaded;
        await registry.DeleteAsync(ollama.Id, CancellationToken.None);
        control.Unloaded.Clear();
        await held.UnloadAsync(CancellationToken.None);
        control.Unloaded.ShouldBe(["Ollama qwen3:8b"]);
    }

    [Fact]
    public async Task AServerThatMovesToThisComputerGivesModelsThatTakeTheCard()
    {
        var registry = new EndpointRegistry(new MemoryEndpoints(), NullLogger<EndpointRegistry>.Instance);
        var box = new LocalEndpoint { Name = "Ollama", BaseUrl = "http://192.168.1.10:11434/v1", Models = [new EndpointModel { Id = "qwen3:8b" }] };
        await registry.SaveAsync(box, CancellationToken.None);
        var directory = new ModelDirectory([new EndpointModelSource(registry, [new RecordingControl()], NullLogger<EndpointModelSource>.Instance)], registry);
        var entry = EndpointCodes.Catalog(box)[0];
        directory.Get(entry).ShouldBeOfType<OnlineModel>();

        box.BaseUrl = "http://localhost:11434/v1";
        await registry.SaveAsync(box, CancellationToken.None);

        directory.Get(entry).ShouldBeOfType<EndpointLocalModel>("the same endpoint, now on this computer: its models wait for the card");
    }

    [Fact]
    public async Task ModelsLeftInLocalServersFromBeforeAreUnloadedExceptTheOneAboutToBeUsed()
    {
        var registry = new EndpointRegistry(new MemoryEndpoints(), NullLogger<EndpointRegistry>.Instance);
        var studio = new LocalEndpoint { Name = "LM Studio", Kind = EndpointKind.LmStudio, BaseUrl = "http://127.0.0.1:1234/v1" };
        var remote = new LocalEndpoint { Name = "Box", Kind = EndpointKind.Ollama, BaseUrl = "http://192.168.1.10:11434/v1" };
        await registry.SaveAsync(studio, CancellationToken.None);
        await registry.SaveAsync(remote, CancellationToken.None);
        var control = new RecordingControl();
        var probe = new LoadedProbe(["qwen/qwen3-8b", "google/gemma-3-12b"]);
        var sweep = new EndpointResidentSweep(registry, probe, [control], NullLogger<EndpointResidentSweep>.Instance);
        var keep = new OnlineModel(new CatalogModel { Provider = EndpointCodes.For(studio.Id), Id = "google/gemma-3-12b" }, false);

        await sweep.SweepAsync(keep, CancellationToken.None);

        control.Unloaded.ShouldBe(["LM Studio qwen/qwen3-8b"], "the model about to be used stays; a server on another computer is not asked");
        probe.Asked.ShouldBe(["LM Studio"]);
    }

    private sealed class RecordingControl : IEndpointModelControl
    {
        public List<string> Unloaded { get; } = [];

        public EndpointUnload Answer { get; set; } = EndpointUnload.Unloaded;

        public Task<EndpointUnload> UnloadAsync(LocalEndpoint endpoint, string modelId, CancellationToken cancellationToken)
        {
            Unloaded.Add(endpoint.Name + " " + modelId);
            return Task.FromResult(Answer);
        }
    }

    private sealed class NotMineControl : IEndpointModelControl
    {
        public Task<EndpointUnload> UnloadAsync(LocalEndpoint endpoint, string modelId, CancellationToken cancellationToken) =>
            Task.FromResult(EndpointUnload.NotMine);
    }

    private sealed class LoadedProbe(IReadOnlyList<string> loaded) : IEndpointProbe
    {
        public List<string> Asked { get; } = [];

        public Task<EndpointListing> ListModelsAsync(string baseUrl, string token, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<DetectedServer>> DetectAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<LoadedServerModel>> LoadedAsync(LocalEndpoint endpoint, CancellationToken cancellationToken)
        {
            Asked.Add(endpoint.Name);
            return Task.FromResult<IReadOnlyList<LoadedServerModel>>(loaded.Select(id => new LoadedServerModel(id, 0, 0, 0)).ToList());
        }
    }

    private sealed class MemoryEndpoints : IEndpointStore
    {
        private readonly List<LocalEndpoint> _all = [];

        public Task<IReadOnlyList<LocalEndpoint>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<LocalEndpoint>>(_all.ToList());

        public Task SaveAsync(LocalEndpoint endpoint, CancellationToken cancellationToken)
        {
            _all.RemoveAll(e => e.Id == endpoint.Id);
            _all.Add(endpoint);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            _all.RemoveAll(e => e.Id == id);
            return Task.CompletedTask;
        }
    }
}