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

    /// <summary>Documents the user added (only ticking a document's row deletes it; the clean-ups take only leftovers).</summary>
    Documents,

    /// <summary>
    /// Files in the video library no record points at any more (video folders a delete before 0.1.28 kept on purpose, images no profile
    /// uses): only ticking their row deletes them.
    /// </summary>
    Library,
}
