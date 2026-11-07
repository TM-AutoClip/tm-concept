namespace TMConcept.Tests.Storage;

/// <summary>A foreign key of a table (<c>pragma_foreign_key_list</c>).</summary>
internal sealed class ForeignKeyRow
{
    public string Table { get; set; } = string.Empty;

    public string References { get; set; } = string.Empty;

    public string From { get; set; } = string.Empty;

    public string To { get; set; } = string.Empty;

    public string OnDelete { get; set; } = string.Empty;
}
