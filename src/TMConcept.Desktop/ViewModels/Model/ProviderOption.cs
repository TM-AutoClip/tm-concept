using TMConcept.Core.Providers;

namespace TMConcept.Desktop.ViewModels;

/// <summary>A provider in a picker.</summary>
public sealed record ProviderOption(IProvider Provider)
{
    /// <summary>Name.</summary>
    public string Name => Provider.Name;
}
