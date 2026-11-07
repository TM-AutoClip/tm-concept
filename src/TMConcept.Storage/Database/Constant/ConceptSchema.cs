namespace TMConcept.Storage.Database;

/// <summary>
/// Names and versions of the database file. The tables are those <see cref="ConceptDbContext"/> maps (EF Core creates them for a new file,
/// the same tables EF Core created for 0.1.0–0.1.2); an older file is brought to the same shape by the upgrade steps
/// (<see cref="SchemaUpgradeBase"/>), one version after the other. Versions: 1 = 0.1.0–0.1.4 (no version written), 2 = 0.1.5
/// (<c>profile_step_models</c>; no version written either: told apart by that table), 3 = 0.1.6 (the models are chosen on the pages and
/// kept per profile only: the general defaults of <c>step_models</c> are copied into every profile that had no choice of its own, then the
/// table goes). 0.1.6 wrote the version as <c>PRAGMA user_version</c>; since 0.1.7 it is the settings row <see cref="VersionSetting"/>
/// (written through EF Core, which has no way to set a pragma), and both are read.
/// </summary>
internal static class ConceptSchema
{
    /// <summary>The version a new file is created with and every older file is brought to.</summary>
    public const int Version = 3;

    /// <summary>The settings row that holds the schema version (0.1.7 on).</summary>
    public const string VersionSetting = "schema-version";

    /// <summary>The value of <c>pragma_secure_delete</c> when deleted content is overwritten with zeros.</summary>
    public const long SecureDeleteOn = 1;

    /// <summary>Money as stored (invariant text, as EF Core's SQLite provider wrote it in 0.1.0–0.1.2).</summary>
    public const string MoneyFormat = "0.0###########################";

    /// <summary>Ids as stored (upper-case text).</summary>
    public const string IdFormat = "D";

    /// <summary><c>sqlite_master</c> type of a table.</summary>
    public const string TableType = "table";

    /// <summary>Profiles.</summary>
    public const string Profiles = "profiles";

    /// <summary>A profile's step choices (0.1.5 on; a file of 0.1.0–0.1.4 lacks it).</summary>
    public const string ProfileStepModels = "profile_step_models";

    /// <summary>The general step defaults of 0.1.0–0.1.5 (removed in version 3).</summary>
    public const string GeneralStepModels = "step_models";

    /// <summary>The price overrides of 0.1.0–0.1.4 (removed in version 2, not carried over).</summary>
    public const string PriceOverrides = "model_overrides";

    /// <summary>Settings.</summary>
    public const string Settings = "settings";

    /// <summary>The voice columns of a 0.1.0–0.1.4 profile (removed in version 2, not carried over).</summary>
    public static readonly IReadOnlyList<string> RemovedProfileColumns = ["SpeechEngine", "SpeechVoice", "SpeechSpeed"];

    /// <summary>General step defaults removed in version 2 (the image and voice choices became page choices).</summary>
    public static readonly IReadOnlyList<string> RemovedSteps = ["image-prompt", "image", "speech"];
}
