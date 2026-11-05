using Microsoft.Data.Sqlite;
using TMConcept.Core.Keys;

namespace TMConcept.Storage.Database;

/// <summary>The <c>api_keys</c> table (<see cref="ApiKey"/>).</summary>
internal sealed class ApiKeyTable : TableBase<ApiKey>
{
    /// <summary>The table.</summary>
    public static ApiKeyTable Instance { get; } = new();

    /// <inheritdoc />
    public override string Name => "api_keys";

    /// <inheritdoc />
    public override IReadOnlyList<string> Columns { get; } = ["Id", "Provider", "Label", "Secret", "CreatedAtUtc", "LastOkAtUtc"];

    /// <inheritdoc />
    public override IReadOnlyList<string> Key { get; } = ["Id"];

    /// <inheritdoc />
    public override ApiKey Read(SqliteDataReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return new ApiKey
        {
            Id = Sql.ReadId(reader, 0),
            Provider = reader.GetString(1),
            Label = reader.GetString(2),
            Secret = reader.GetString(3),
            CreatedAtUtc = Sql.ReadTime(reader, 4),
            LastOkAtUtc = Sql.ReadOptionalTime(reader, 5),
        };
    }

    /// <inheritdoc />
    public override object?[] Values(ApiKey row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return
        [
            Sql.Id(row.Id),
            row.Provider,
            row.Label,
            row.Secret,
            Sql.Ticks(row.CreatedAtUtc),
            Sql.Ticks(row.LastOkAtUtc),
        ];
    }
}
