using Microsoft.Data.Sqlite;

namespace TMConcept.Storage.Database;

/// <summary>The <c>settings</c> table (<see cref="SettingRow"/>).</summary>
internal sealed class SettingTable : TableBase<SettingRow>
{
    /// <summary>The table.</summary>
    public static SettingTable Instance { get; } = new();

    /// <inheritdoc />
    public override string Name => "settings";

    /// <inheritdoc />
    public override IReadOnlyList<string> Columns { get; } = ["Key", "Value"];

    /// <inheritdoc />
    public override IReadOnlyList<string> Key { get; } = ["Key"];

    /// <inheritdoc />
    public override SettingRow Read(SqliteDataReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return new SettingRow
        {
            Key = reader.GetString(0),
            Value = reader.GetString(1),
        };
    }

    /// <inheritdoc />
    public override object?[] Values(SettingRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return
        [
            row.Key,
            row.Value,
        ];
    }
}
