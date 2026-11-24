using System.Security.Cryptography;
using System.Text;

namespace TMConcept.Providers.GoogleAuth;

/// <summary>
/// PKCE (RFC 7636) with S256, and the state of one sign-in: random values from the system's cryptographic generator, base64url without
/// padding. A verifier is 43 characters (32 random bytes), inside the 43–128 the RFC allows.
/// </summary>
public static class Pkce
{
    /// <summary>A new code verifier.</summary>
    public static string NewVerifier() => Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>The S256 challenge of <paramref name="verifier"/>: base64url(SHA-256(ASCII(verifier))).</summary>
    public static string Challenge(string verifier)
    {
        ArgumentException.ThrowIfNullOrEmpty(verifier);
        return Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    }

    /// <summary>A new state (the answer to the local address must carry it back).</summary>
    public static string NewState() => Base64Url(RandomNumberGenerator.GetBytes(24));

    /// <summary>Whether a returned state is the expected one (compared in fixed time).</summary>
    public static bool SameState(string expected, string? returned) =>
        returned is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(returned));

    /// <summary>Base64url without padding.</summary>
    public static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
