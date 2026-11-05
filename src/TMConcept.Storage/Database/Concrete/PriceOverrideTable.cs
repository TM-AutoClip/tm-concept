using Microsoft.Data.Sqlite;
using TMConcept.Core.Models;

namespace TMConcept.Storage.Database;

/// <summary>The <c>model_overrides</c> table (<see cref="ModelPriceOverride"/>).</summary>
internal sealed class PriceOverrideTable : TableBase<ModelPriceOverride>
{
    /// <summary>The table.</summary>
    public static PriceOverrideTable Instance { get; } = new();

    /// <inheritdoc />
    public override string Name => "model_overrides";

    /// <inheritdoc />
    public override IReadOnlyList<string> Columns { get; } = ["Provider", "ModelId", "InputUsd", "OutputUsd", "CacheReadUsd", "Note", "UpdatedAtUtc"];

    /// <inheritdoc />
    public override IReadOnlyList<string> Key { get; } = ["Provider", "ModelId"];

    /// <inheritdoc />
    public override ModelPriceOverride Read(SqliteDataReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return new ModelPriceOverride
        {
            Provider = reader.GetString(0),
            ModelId = reader.GetString(1),
            InputUsd = Sql.ReadMoney(reader, 2),
            OutputUsd = Sql.ReadMoney(reader, 3),
            CacheReadUsd = Sql.ReadMoney(reader, 4),
            Note = reader.GetString(5),
            UpdatedAtUtc = Sql.ReadTime(reader, 6),
        };
    }

    /// <inheritdoc />
    public override object?[] Values(ModelPriceOverride row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return
        [
            row.Provider,
            row.ModelId,
            Sql.Money(row.InputUsd),
            Sql.Money(row.OutputUsd),
            Sql.Money(row.CacheReadUsd),
            row.Note,
            Sql.Ticks(row.UpdatedAtUtc),
        ];
    }
}
