using TMConcept.Storage.Keys;

namespace TMConcept.Tests.Storage;

/// <summary>A master password source that always answers the same password.</summary>
internal sealed class FixedPasswords(string password) : IMasterPasswordSource
{
    public string Password { get; set; } = password;

    public Task<string> GetAsync(bool confirm, CancellationToken cancellationToken) => Task.FromResult(Password);
}
