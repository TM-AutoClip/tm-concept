using System.Security.Cryptography;
using System.Text;

namespace TMConcept.Storage.Keys;

/// <summary>
/// Windows DPAPI (current user): only the same Windows user on this computer can open the key. Extra entropy is given so another program
/// of the same user cannot open the copied file on its own.
/// </summary>
public sealed class DpapiKeyProtector : IKeyProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TM Concept database key v1");

    /// <inheritdoc />
    public string Method => KeyMethods.Dpapi;

    /// <inheritdoc />
    public bool IsAvailable => OperatingSystem.IsWindows();

    /// <inheritdoc />
    public bool Automatic => true;

    /// <inheritdoc />
    public Task<KeyEnvelope> ProtectAsync(byte[] key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Task.FromResult(new KeyEnvelope(Method, Convert.ToBase64String(Protect(key))));
    }

    /// <inheritdoc />
    public Task<byte[]> UnprotectAsync(KeyEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return Task.FromResult(Unprotect(Convert.FromBase64String(envelope.Payload)));
    }

    /// <inheritdoc />
    public Task ForgetAsync(KeyEnvelope envelope, CancellationToken cancellationToken) => Task.CompletedTask;

    private static byte[] Protect(byte[] key)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("DPAPI is only available on Windows.");
        }

        return ProtectedData.Protect(key, Entropy, DataProtectionScope.CurrentUser);
    }

    private static byte[] Unprotect(byte[] data)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("DPAPI is only available on Windows.");
        }

        return ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
    }
}
