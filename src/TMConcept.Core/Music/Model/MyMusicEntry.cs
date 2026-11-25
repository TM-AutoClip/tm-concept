namespace TMConcept.Core.Music;

/// <summary>
/// One piece of the user's own music collection (0.1.54, "Müziklerim"): its track (id <c>mine:&lt;guid&gt;</c>, the file's full path in
/// <c>&lt;data&gt;/my-music</c>) and where it came from — a file the user added, or a piece made on the "Müzik üret" page with its model,
/// prompt, reference sound and cost.
/// </summary>
public sealed class MyMusicEntry
{
    /// <summary>The track as the music choice and the video use it.</summary>
    public MusicTrack Track { get; set; } = new();

    /// <summary>Made by a music model (true) or a file the user added (false).</summary>
    public bool Generated { get; set; }

    /// <summary>The music model's catalog key ("local/ace-step-1.5"); empty for an added file.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>The prompt as the user wrote it; empty for an added file.</summary>
    public string Prompt { get; set; } = string.Empty;

    /// <summary>The name of the reference sound the tempo came from; empty when none.</summary>
    public string ReferenceName { get; set; } = string.Empty;

    /// <summary>What it cost (USD; 0 for the local model and an added file).</summary>
    public decimal Usd { get; set; }

    /// <summary>When it was added (UTC).</summary>
    public DateTimeOffset AddedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
