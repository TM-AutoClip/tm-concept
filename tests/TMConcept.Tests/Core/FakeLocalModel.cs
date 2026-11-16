using TMConcept.Core.Models;

namespace TMConcept.Tests.Core;

/// <summary>A local model that only counts its loads and unloads (and can be told to fail unloading).</summary>
internal sealed class FakeLocalModel(string id, ModelDevice device = ModelDevice.Gpu, int videoGb = 6)
    : LocalModelBase(new CatalogModel { Provider = ModelProviders.Local, Id = id, Name = id, Capabilities = ModelCapability.Image, MinVideoMemoryGb = videoGb })
{
    public int Loads { get; private set; }

    public int Unloads { get; private set; }

    public List<string> Events { get; } = [];

    public bool FailUnload { get; set; }

    public override ModelDevice Device => device;

    protected override Task OnLoadAsync(CancellationToken cancellationToken)
    {
        Loads++;
        Events.Add("load " + Id);
        return Task.CompletedTask;
    }

    protected override Task OnUnloadAsync(CancellationToken cancellationToken)
    {
        Unloads++;
        Events.Add("unload " + Id);
        return FailUnload ? throw new InvalidOperationException("the server did not answer") : Task.CompletedTask;
    }
}