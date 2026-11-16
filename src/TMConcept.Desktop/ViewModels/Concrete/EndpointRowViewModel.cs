using CommunityToolkit.Mvvm.ComponentModel;
using TMConcept.Core.Endpoints;
using TMConcept.Core.Security;

namespace TMConcept.Desktop.ViewModels;

/// <summary>A saved local server (the token shown masked).</summary>
public sealed partial class EndpointRowViewModel(LocalEndpoint endpoint) : ObservableObject
{
    /// <summary>The endpoint.</summary>
    public LocalEndpoint Endpoint { get; } = endpoint;

    /// <summary>Name.</summary>
    public string Name => Endpoint.Name;

    /// <summary>Address, with the server's kind when the app speaks its own API (Ollama, LM Studio).</summary>
    public string Address => Endpoint.Kind == EndpointKind.Generic ? Endpoint.BaseUrl : EndpointEditorViewModel.KindName(Endpoint.Kind) + " · " + Endpoint.BaseUrl;

    /// <summary>The kept models, one line.</summary>
    public string Models => string.Join(", ", Endpoint.Models.Select(m => m.Id));

    /// <summary>Masked token (empty without one).</summary>
    public string Token => Redactor.Mask(Endpoint.Token);

    /// <summary>Last test result (✓ / ✗ with a short reason).</summary>
    [ObservableProperty]
    public partial string Result { get; set; } = string.Empty;

    /// <summary>Whether the last test failed.</summary>
    [ObservableProperty]
    public partial bool Failed { get; set; }
}
