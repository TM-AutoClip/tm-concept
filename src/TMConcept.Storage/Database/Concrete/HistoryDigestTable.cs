using Microsoft.Data.Sqlite;
using TMConcept.Core.Duplicates;

namespace TMConcept.Storage.Database;

/// <summary>The <c>history_digests</c> table (<see cref="HistoryDigest"/>).</summary>
internal sealed class HistoryDigestTable : TableBase<HistoryDigest>
{
    /// <summary>The table.</summary>
    public static HistoryDigestTable Instance { get; } = new();

    /// <inheritdoc />
    public override string Name => "history_digests";

    /// <inheritdoc />
    public override IReadOnlyList<string> Columns { get; } = ["ProfileId", "Version", "Text", "CoversUntilNumber", "UpdatedAtUtc"];

    /// <inheritdoc />
    public override IReadOnlyList<string> Key { get; } = ["ProfileId"];

    /// <inheritdoc />
    public override HistoryDigest Read(SqliteDataReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return new HistoryDigest
        {
            ProfileId = Sql.ReadId(reader, 0),
            Version = reader.GetInt32(1),
            Text = reader.GetString(2),
            CoversUntilNumber = reader.GetInt32(3),
            UpdatedAtUtc = Sql.ReadTime(reader, 4),
        };
    }

    /// <inheritdoc />
    public override object?[] Values(HistoryDigest row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return
        [
            Sql.Id(row.ProfileId),
            row.Version,
            row.Text,
            row.CoversUntilNumber,
            Sql.Ticks(row.UpdatedAtUtc),
        ];
    }
}
