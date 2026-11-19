namespace TMConcept.Storage.Database;

/// <summary>
/// Names and stored forms of the database file. The tables are those <see cref="ConceptDbContext"/> maps; the schema steps
/// (<see cref="SchemaUpgrader"/>, EF Core migrations) create and upgrade them, and the file records which steps it went through (since
/// 0.1.17; until then no version was written and older files were not recognised).
/// </summary>
internal static class ConceptSchema
{
    /// <summary>The value of <c>pragma_secure_delete</c> when deleted content is overwritten with zeros.</summary>
    public const long SecureDeleteOn = 1;

    /// <summary>
    /// How long a connection waits for another connection's lock before "database is locked" (ms). Pieces of work are short (a few ms to
    /// tens of ms), so a wait this long only ends in an error when something is really stuck.
    /// </summary>
    public const int BusyWaitMilliseconds = 10_000;

    /// <summary>Money as stored (invariant text).</summary>
    public const string MoneyFormat = "0.0###########################";

    /// <summary>Ids as stored (upper-case text).</summary>
    public const string IdFormat = "D";

    /// <summary><c>sqlite_master</c> type of a table.</summary>
    public const string TableType = "table";

    /// <summary>The start of SQLite's own table names (<c>sqlite_sequence</c>): not the app's tables.</summary>
    public const string SqliteOwnPrefix = "sqlite_";

    /// <summary>The start of the tables where EF Core records the schema steps (<c>__EFMigrationsHistory</c>, <c>__EFMigrationsLock</c>).</summary>
    public const string StepRecordPrefix = "__EFMigrations";

    /// <summary>The working copy a file is upgraded in, next to it (replaces the file only when every step went through).</summary>
    public const string UpgradingSuffix = ".upgrading";

    /// <summary>The folder (under the backup folder) of the copies made before an upgrade.</summary>
    public const string UpgradeBackupFolder = "before-upgrade";

    /// <summary>The start of the name of a copy made before an upgrade (<c>concept-20261116-120000-000-Version0117.db</c>).</summary>
    public const string UpgradeBackupPrefix = "concept-";

    /// <summary>The extension of a copy made before an upgrade (an encrypted database file, opened with the same key).</summary>
    public const string UpgradeBackupExtension = ".db";

    /// <summary>How many copies made before an upgrade are kept (the newest).</summary>
    public const int UpgradeBackupsKept = 5;

    /// <summary>Profiles.</summary>
    public const string Profiles = "profiles";

    /// <summary>A profile's step choices.</summary>
    public const string ProfileStepModels = "profile_step_models";

    /// <summary>Settings.</summary>
    public const string Settings = "settings";
}
