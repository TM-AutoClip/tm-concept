using System.Text;
using TMConcept.Storage.Crypto;

namespace TMConcept.Storage.Keys;

/// <summary>
/// Master password protection, on every operating system: the key is sealed with Argon2id + AES-256-GCM (<see cref="SealedBox"/>). Asked
/// at every start. If the password is forgotten the data cannot be recovered (the window says so).
/// </summary>
public sealed class PasswordKeyProtector(IMasterPasswordSource passwords, KdfParameters kdf) : IKeyProtector
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("TMCKEY01");

    /// <inheritdoc />
    public string Method => KeyMethods.Password;

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public bool Automatic => false;

    /// <inheritdoc />
    public async Task<KeyEnvelope> ProtectAsync(byte[] key, CancellationToken cancellationToken)
    {
        var password = await passwords.GetAsync(confirm: true, cancellationToken).ConfigureAwait(false);
        return new KeyEnvelope(Method, Convert.ToBase64String(SealedBox.Seal(Magic, password, key, kdf)));
    }

    /// <inheritdoc />
    public async Task<byte[]> UnprotectAsync(KeyEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var password = await passwords.GetAsync(confirm: false, cancellationToken).ConfigureAwait(false);
        return SealedBox.Open(Magic, password, Convert.FromBase64String(envelope.Payload));
    }

    /// <inheritdoc />
    public Task ForgetAsync(KeyEnvelope envelope, CancellationToken cancellationToken) => Task.CompletedTask;
}
