using Microsoft.Data.Sqlite;
using TMConcept.Core.Concepts;

namespace TMConcept.Storage.Database;

/// <summary>The <c>concepts</c> table (<see cref="Concept"/>).</summary>
internal sealed class ConceptTable : TableBase<Concept>
{
    /// <summary>The table.</summary>
    public static ConceptTable Instance { get; } = new();

    /// <inheritdoc />
    public override string Name => "concepts";

    /// <inheritdoc />
    public override IReadOnlyList<string> Columns { get; } = ["Id", "ProfileId", "Number", "Status", "Title", "Summary", "Content", "Language", "RunId", "SimilarToId", "Similarity", "DuplicateNote", "CreatedAtUtc", "DecidedAtUtc"];

    /// <inheritdoc />
    public override IReadOnlyList<string> Key { get; } = ["Id"];

    /// <summary><see cref="TableBase{T}.Select"/> with an empty content (lists do not read the long text).</summary>
    public string SelectWithoutContent => Select.Replace("\"Content\"", "'' AS \"Content\"", StringComparison.Ordinal);

    /// <inheritdoc />
    public override Concept Read(SqliteDataReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return new Concept
        {
            Id = Sql.ReadId(reader, 0),
            ProfileId = Sql.ReadId(reader, 1),
            Number = reader.GetInt32(2),
            Status = (ConceptStatus)reader.GetInt32(3),
            Title = reader.GetString(4),
            Summary = reader.GetString(5),
            Content = reader.GetString(6),
            Language = reader.GetString(7),
            RunId = Sql.ReadOptionalId(reader, 8),
            SimilarToId = Sql.ReadOptionalId(reader, 9),
            Similarity = Sql.ReadOptionalReal(reader, 10),
            DuplicateNote = reader.GetString(11),
            CreatedAtUtc = Sql.ReadTime(reader, 12),
            DecidedAtUtc = Sql.ReadOptionalTime(reader, 13),
        };
    }

    /// <inheritdoc />
    public override object?[] Values(Concept row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return
        [
            Sql.Id(row.Id),
            Sql.Id(row.ProfileId),
            row.Number,
            (int)row.Status,
            row.Title,
            row.Summary,
            row.Content,
            row.Language,
            Sql.Id(row.RunId),
            Sql.Id(row.SimilarToId),
            row.Similarity,
            row.DuplicateNote,
            Sql.Ticks(row.CreatedAtUtc),
            Sql.Ticks(row.DecidedAtUtc),
        ];
    }
}
