namespace TMConcept.Core.Keys;

/// <summary>
/// An API key of a provider. The value lives in the encrypted database (and in a password-encrypted backup only when the user ticks the
/// keys), never in logs or errors; it is sent only to its own
/// provider's fixed address. A provider can have several keys with different labels.
/// </summary>
public sealed class ApiKey
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Provider code.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Label ("personal", "work").</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>The key.</summary>
    public string Secret { get; set; } = string.Empty;

    /// <summary>Added (UTC).</summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Last successful connection test (UTC).</summary>
    public DateTimeOffset? LastOkAtUtc { get; set; }
}
