using System.Security.Cryptography;

namespace TMConcept.Storage.Keys;

/// <summary>
/// Linux Secret Service (GNOME Keyring, KWallet, KeePassXC...) through <c>secret-tool</c> from libsecret. The key goes to the keyring through
/// standard input, never as an argument. The key file keeps only the entry's lookup id. Not available without <c>secret-tool</c> or a D-Bus
/// session: the app then uses a master password; it never falls back to a plain file.
/// </summary>
public sealed class SecretServiceKeyProtector(ICommandRunner runner) : IKeyProtector
{
    private const string Tool = "secret-tool";
    private const string Application = "tm-concept";

    /// <inheritdoc />
    public string Method => KeyMethods.SecretService;

    /// <inheritdoc />
    public bool IsAvailable =>
        OperatingSystem.IsLinux() && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS")) && runner.Exists(Tool);

    /// <inheritdoc />
    public bool Automatic => true;

    /// <inheritdoc />
    public async Task<KeyEnvelope> ProtectAsync(byte[] key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        var (code, _, error) = await runner.RunAsync(Tool,
            ["store", "--label=TM Concept database key", "application", Application, "key", id], Convert.ToBase64String(key), cancellationToken)
            .ConfigureAwait(false);
        return code == 0
            ? new KeyEnvelope(Method, id)
            : throw new InvalidOperationException($"secret-tool could not store the key (exit {code}): {error.Trim()}");
    }

    /// <inheritdoc />
    public async Task<byte[]> UnprotectAsync(KeyEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var (code, output, error) = await runner.RunAsync(Tool, ["lookup", "application", Application, "key", envelope.Payload], null, cancellationToken)
            .ConfigureAwait(false);
        if (code != 0 || output.Trim().Length == 0)
        {
            throw new InvalidOperationException($"The database key was not found in the keyring (secret-tool exit {code}): {error.Trim()}");
        }

        return Convert.FromBase64String(output.Trim());
    }

    /// <inheritdoc />
    public async Task ForgetAsync(KeyEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        await runner.RunAsync(Tool, ["clear", "application", Application, "key", envelope.Payload], null, cancellationToken).ConfigureAwait(false);
    }
}
