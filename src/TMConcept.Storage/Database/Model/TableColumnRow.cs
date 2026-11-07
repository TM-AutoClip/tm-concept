namespace TMConcept.Storage.Database;

/// <summary>A column of a table (<c>pragma_table_info</c>, read only; <see cref="Table"/> is its hidden argument column).</summary>
internal sealed class TableColumnRow
{
    /// <summary>The table (the pragma's argument).</summary>
    public string Table { get; set; } = string.Empty;

    /// <summary>Position.</summary>
    public long Position { get; set; }

    /// <summary>Name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Declared type.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>1 when NOT NULL.</summary>
    public long NotNull { get; set; }

    /// <summary>Position in the primary key (0 = not part of it).</summary>
    public long Key { get; set; }
}
