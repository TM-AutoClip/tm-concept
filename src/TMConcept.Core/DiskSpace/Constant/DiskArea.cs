namespace TMConcept.Core.DiskSpace;

/// <summary>Where on disk something lives, for the storage totals (Settings → Storage).</summary>
public enum DiskArea
{
    /// <summary>Programs and libraries the app downloaded (<c>tools/</c>, without the models), old components folders included.</summary>
    Tools,

    /// <summary>Model weights (voices, image models, the meaning-check model).</summary>
    Models,

    /// <summary>Work caches (<c>work/</c>: cached narration, run folders, scene images, voice sample files).</summary>
    Work,

    /// <summary>Backups (<c>backups/</c>; never removed by the storage manager).</summary>
    Backups,

    /// <summary>Logs and problem reports (<c>logs/</c>, <c>reports/</c>; never removed by the storage manager).</summary>
    Logs,

    /// <summary>Documents the user added (a later version; the full delete will take them too).</summary>
    Documents,
}
