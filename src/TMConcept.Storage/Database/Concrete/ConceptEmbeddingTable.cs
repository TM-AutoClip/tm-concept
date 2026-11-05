using Microsoft.Data.Sqlite;
using TMConcept.Core.Duplicates;

namespace TMConcept.Storage.Database;

/// <summary>The <c>concept_embeddings</c> table (<see cref="ConceptEmbedding"/>).</summary>
internal sealed class ConceptEmbeddingTable : TableBase<ConceptEmbedding>
{
    /// <summary>The table.</summary>
    public static ConceptEmbeddingTable Instance { get; } = new();

    /// <inheritdoc />
    public override string Name => "concept_embeddings";

    /// <inheritdoc />
    public override IReadOnlyList<string> Columns { get; } = ["ConceptId", "ProfileId", "Model", "Dimensions", "Vector"];

    /// <inheritdoc />
    public override IReadOnlyList<string> Key { get; } = ["ConceptId"];

    /// <inheritdoc />
    public override ConceptEmbedding Read(SqliteDataReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return new ConceptEmbedding
        {
            ConceptId = Sql.ReadId(reader, 0),
            ProfileId = Sql.ReadId(reader, 1),
            Model = reader.GetString(2),
            Dimensions = reader.GetInt32(3),
            Vector = (byte[])reader.GetValue(4),
        };
    }

    /// <inheritdoc />
    public override object?[] Values(ConceptEmbedding row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return
        [
            Sql.Id(row.ConceptId),
            Sql.Id(row.ProfileId),
            row.Model,
            row.Dimensions,
            row.Vector,
        ];
    }
}
