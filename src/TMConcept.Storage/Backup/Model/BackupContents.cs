namespace TMConcept.Storage.Backup;

/// <summary>
/// What goes into a backup. Concepts belong to profiles, so <see cref="Concepts"/> needs <see cref="Profiles"/>. API keys go in only when
/// chosen: the file is encrypted with the backup password, but whoever has file and password can use them. Automatic backups (daily, before
/// a large change) default to everything except the keys.
/// </summary>
public sealed class BackupContents
{
    /// <summary>API keys.</summary>
    public bool Keys { get; set; }

    /// <summary>Profiles with their model choices and sample images (the image files themselves).</summary>
    public bool Profiles { get; set; } = true;

    /// <summary>Concepts (also the trash), the queue, the video list, the cost history.</summary>
    public bool Concepts { get; set; } = true;

    /// <summary>The app's settings.</summary>
    public bool Settings { get; set; } = true;

    /// <summary>The default of automatic backups: everything except the API keys.</summary>
    public static BackupContents Automatic => new();

    /// <summary>Everything a backup of 0.1.1–0.1.5 held (no keys, no image files).</summary>
    public static BackupContents Version2 => new() { Keys = false, Profiles = true, Concepts = true, Settings = true };

    /// <summary>Whether something is chosen and the choice holds together (concepts need their profiles).</summary>
    public bool IsValid => (Keys || Profiles || Concepts || Settings) && (!Concepts || Profiles);

    /// <summary>For the log: "keys, profiles, concepts, settings" (only what is in).</summary>
    public override string ToString() => string.Join(", ", new[] { (Keys, "keys"), (Profiles, "profiles"), (Concepts, "concepts"), (Settings, "settings") }
        .Where(p => p.Item1).Select(p => p.Item2));
}
