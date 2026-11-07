namespace TMConcept.Storage.Database;

/// <summary>A row of <c>sqlite_master</c> (read only): which tables and indexes a file has.</summary>
internal sealed class SchemaObjectRow
{
    /// <summary>"table", "index", "view" or "trigger".</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The table an index belongs to (the table's own name for a table).</summary>
    public string Table { get; set; } = string.Empty;
}
