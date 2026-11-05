namespace TMConcept.Storage.Keys;

/// <summary>Names of the key protectors (written to the key file).</summary>
public static class KeyMethods
{
    /// <summary>Windows DPAPI, current user.</summary>
    public const string Dpapi = "dpapi";

    /// <summary>Linux Secret Service (GNOME Keyring, KWallet) through <c>secret-tool</c>.</summary>
    public const string SecretService = "secret-service";

    /// <summary>Master password (Argon2id + AES-256-GCM).</summary>
    public const string Password = "password";
}
