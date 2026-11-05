namespace TMConcept.Storage.Crypto;

/// <summary>A key derived from a password with Argon2id, with the salt and cost needed to derive it again.</summary>
public sealed record DerivedKey(byte[] Salt, KdfParameters Kdf, byte[] Key);
