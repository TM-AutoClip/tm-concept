using CommunityToolkit.Mvvm.ComponentModel;
using TMConcept.Core.Keys;
using TMConcept.Core.Security;

namespace TMConcept.Desktop.ViewModels;

/// <summary>A saved key (shown masked).</summary>
public sealed partial class ApiKeyRowViewModel(ApiKey key, string providerName) : ObservableObject
{
    /// <summary>The key.</summary>
    public ApiKey Key { get; } = key;

    /// <summary>Provider name.</summary>
    public string ProviderName { get; } = providerName;

    /// <summary>Label.</summary>
    public string Label => Key.Label;

    /// <summary>Masked value.</summary>
    public string Masked => Redactor.Mask(Key.Secret);

    /// <summary>Last test result (✓ / ✗ with a short reason).</summary>
    [ObservableProperty]
    public partial string Result { get; set; } = string.Empty;

    /// <summary>Whether the last test failed.</summary>
    [ObservableProperty]
    public partial bool Failed { get; set; }
}
