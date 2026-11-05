using TMConcept.Storage.Keys;

namespace TMConcept.Desktop.Startup;

/// <summary>
/// The master password typed in a window. The window checks the input (twice for a new password) and puts it here right before the vault
/// call that needs it; it is cleared after one use.
/// </summary>
public sealed class UiMasterPassword : IMasterPasswordSource
{
    private string? _next;

    /// <summary>Sets the password for the next request.</summary>
    public void Provide(string password) => _next = password;

    /// <inheritdoc />
    public Task<string> GetAsync(bool confirm, CancellationToken cancellationToken)
    {
        var value = _next ?? throw new InvalidOperationException("No master password was entered.");
        _next = null;
        return Task.FromResult(value);
    }
}
