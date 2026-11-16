namespace TMConcept.Core.Models;

/// <summary>
/// A local model no <see cref="ILocalModelSource"/> claims (a catalog entry whose engine is not registered, as in some tests): taken to run
/// on the processor, with nothing to load or unload, so it never waits for the graphics card.
/// </summary>
public sealed class ProcessorModel(CatalogModel catalog) : LocalModelBase(catalog)
{
    /// <inheritdoc />
    public override ModelDevice Device => ModelDevice.Cpu;
}