using Microsoft.Data.Sqlite;
using TMConcept.Core.Videos;

namespace TMConcept.Storage.Database;

/// <summary>The <c>queue_items</c> table (<see cref="QueueItem"/>).</summary>
internal sealed class QueueItemTable : TableBase<QueueItem>
{
    /// <summary>The table.</summary>
    public static QueueItemTable Instance { get; } = new();

    /// <inheritdoc />
    public override string Name => "queue_items";

    /// <inheritdoc />
    public override IReadOnlyList<string> Columns { get; } = ["Id", "ProfileId", "ConceptId", "Position", "SettingsJson", "AddedAtUtc"];

    /// <inheritdoc />
    public override IReadOnlyList<string> Key { get; } = ["Id"];

    /// <inheritdoc />
    public override QueueItem Read(SqliteDataReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return new QueueItem
        {
            Id = Sql.ReadId(reader, 0),
            ProfileId = Sql.ReadId(reader, 1),
            ConceptId = Sql.ReadId(reader, 2),
            Position = reader.GetInt32(3),
            SettingsJson = reader.GetString(4),
            AddedAtUtc = Sql.ReadTime(reader, 5),
        };
    }

    /// <inheritdoc />
    public override object?[] Values(QueueItem row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return
        [
            Sql.Id(row.Id),
            Sql.Id(row.ProfileId),
            Sql.Id(row.ConceptId),
            row.Position,
            row.SettingsJson,
            Sql.Ticks(row.AddedAtUtc),
        ];
    }
}
