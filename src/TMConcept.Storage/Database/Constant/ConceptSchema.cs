namespace TMConcept.Storage.Database;

/// <summary>
/// Names and stored forms of the database file. The tables are those <see cref="ConceptDbContext"/> maps; EF Core creates them for a new
/// file. No version is written or checked and older files are not recognised (no backward compatibility, 8 Nov 2026).
/// </summary>
internal static class ConceptSchema
{
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
