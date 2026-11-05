namespace TMConcept.Storage.Crypto;

/// <summary>
/// Argon2id cost. Default 64 MiB / 3 passes / 1 lane: above the OWASP minimum (19 MiB / 2 / 1) because the key is derived rarely (opening
/// with a master password, backup, restore). Tests use <see cref="Fast"/>.
/// </summary>
public sealed record KdfParameters(int MemoryKiB, int Iterations, int Parallelism)
{
    /// <summary>Production cost.</summary>
    public static KdfParameters Default { get; } = new(64 * 1024, 3, 1);

    /// <summary>Cheap cost for tests.</summary>
    public static KdfParameters Fast { get; } = new(1024, 1, 1);
}
