namespace TMConcept.Storage.Backup;

/// <summary>The step a restore (or the look at a backup before it) is at, for the line under the first-start form.</summary>
public enum RestoreStage
{
    /// <summary>The file is read and decrypted with the backup password.</summary>
    Opening,

    /// <summary>The database inside is opened and checked.</summary>
    Checking,

    /// <summary>The database is installed into the data folder and its key sealed again.</summary>
    Installing,

    /// <summary>The sample image files are written into the library folder.</summary>
    WritingImages,
}
