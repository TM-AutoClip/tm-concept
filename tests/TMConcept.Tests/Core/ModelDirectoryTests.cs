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

    private sealed class RecordingControl : IEndpointModelControl
    {
        public List<string> Unloaded { get; } = [];

        public Task<bool> UnloadAsync(LocalEndpoint endpoint, string modelId, CancellationToken cancellationToken)
        {
            Unloaded.Add(endpoint.Name + " " + modelId);
            return Task.FromResult(true);
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