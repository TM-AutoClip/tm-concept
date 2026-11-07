namespace TMConcept.Tests.Storage;

/// <summary>A row of <c>sqlite_master</c> with the SQL SQLite keeps for it.</summary>
internal sealed class MasterSqlRow
{
    public string Type { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Sql { get; set; }
}
