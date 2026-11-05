namespace TMConcept.Core.Profiles;

/// <summary>A sample image of a profile: copied into the profile's image folder (unencrypted) with a short caption the user writes.</summary>
public sealed class ProfileImage
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Profile.</summary>
    public Guid ProfileId { get; set; }

    /// <summary>File name inside the profile's image folder (relative; never a full path).</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Short description ("dome from inside").</summary>
    public string Caption { get; set; } = string.Empty;

    /// <summary>Width in pixels.</summary>
    public int Width { get; set; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; set; }

    /// <summary>SHA-256 of the file (hex), so the same image is not added twice.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Added (UTC).</summary>
    public DateTimeOffset AddedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
