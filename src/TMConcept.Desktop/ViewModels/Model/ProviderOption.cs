using TMConcept.Core.Providers;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// A provider in the API keys picker: an online provider (<paramref name="Provider"/> set), or the entry for adding a local server
/// (<see cref="IsEndpoint"/>; no <see cref="IProvider"/>: its address comes from the user).
/// </summary>
public sealed record ProviderOption(string Code, string Name, IProvider? Provider)
{
    /// <summary>An online provider.</summary>
    public ProviderOption(IProvider provider)
        : this(provider?.Code ?? throw new ArgumentNullException(nameof(provider)), provider.Name, provider)
    {
    }

    /// <summary>The "local server (OpenAI-compatible)" entry.</summary>
    public bool IsEndpoint => Provider is null;
}
