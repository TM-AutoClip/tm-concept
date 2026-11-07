namespace TMConcept.Tests.Storage;

/// <summary>An index of a table (<c>pragma_index_list</c>; origin "c" = made by CREATE INDEX).</summary>
internal sealed class IndexRow
{
    public string Table { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public long Unique { get; set; }

    public string Origin { get; set; } = string.Empty;
}
