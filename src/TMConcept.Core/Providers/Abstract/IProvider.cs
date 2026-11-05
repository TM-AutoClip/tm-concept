namespace TMConcept.Core.Providers;

/// <summary>
/// An online provider: its fixed address (never taken from the user), where to get a key, the steps of the key guide, and a free connection
/// test. A new provider = a new class registered in DI; nothing else changes.
/// </summary>
public interface IProvider
{
    /// <summary>Code ("anthropic").</summary>
    string Code { get; }

    /// <summary>Display name.</summary>
    string Name { get; }

    /// <summary>The fixed address keys of this provider are sent to.</summary>
    Uri Address { get; }

    /// <summary>The provider's page where a key is created.</summary>
    Uri KeyPage { get; }

    /// <summary>Whether a free test request exists (a model or key listing).</summary>
    bool CanTest { get; }

    /// <summary>Tests the key with a free request.</summary>
    Task<ConnectionTest> TestAsync(string apiKey, CancellationToken cancellationToken);
}
