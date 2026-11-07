namespace TMConcept.Storage.Database;

/// <summary>
/// Names and the version of the database file. The tables are those <see cref="ConceptDbContext"/> maps; EF Core creates them for a new
/// file and writes <see cref="Version"/> into the settings row <see cref="VersionSetting"/>. Only a file of exactly this version is opened:
/// there is no upgrade from older files and no reading of their layout (no backward compatibility, 7 Nov 2026). A file without the row
/// (0.1.0–0.1.6) or with another number is refused with <see cref="DatabaseVersionException"/>, and the app offers to set it aside and
/// start fresh. Change <see cref="Version"/> whenever the mapping changes.
/// </summary>
internal static class ConceptSchema
{
    /// <summary>The version a new file is created with and the only one that is opened.</summary>
    public const int Version = 3;

    /// <summary>The settings row that holds the schema version.</summary>
    public const string VersionSetting = "schema-version";

    /// <summary>The value of <c>pragma_secure_delete</c> when deleted content is overwritten with zeros.</summary>
    public const long SecureDeleteOn = 1;

    /// <summary>Money as stored (invariant text).</summary>
    public const string MoneyFormat = "0.0###########################";

    /// <summary>Ids as stored (upper-case text).</summary>
    public const string IdFormat = "D";

    /// <summary><c>sqlite_master</c> type of a table.</summary>
    public const string TableType = "table";

    /// <summary>Profiles.</summary>
    public const string Profiles = "profiles";

    /// <summary>A profile's step choices.</summary>
    public const string ProfileStepModels = "profile_step_models";

    /// <summary>Settings.</summary>
    public const string Settings = "settings";
}
