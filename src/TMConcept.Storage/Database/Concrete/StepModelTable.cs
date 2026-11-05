using Microsoft.Data.Sqlite;
using TMConcept.Core.Models;

namespace TMConcept.Storage.Database;

/// <summary>The <c>step_models</c> table (<see cref="StepModelChoice"/>).</summary>
internal sealed class StepModelTable : TableBase<StepModelChoice>
{
    /// <summary>The table.</summary>
    public static StepModelTable Instance { get; } = new();

    /// <inheritdoc />
    public override string Name => "step_models";

    /// <inheritdoc />
    public override IReadOnlyList<string> Columns { get; } = ["Step", "Provider", "Model", "KeyId", "OptionsJson"];

    /// <inheritdoc />
    public override IReadOnlyList<string> Key { get; } = ["Step"];

    /// <inheritdoc />
    public override StepModelChoice Read(SqliteDataReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return new StepModelChoice
        {
            Step = reader.GetString(0),
            Provider = reader.GetString(1),
            Model = reader.GetString(2),
            KeyId = Sql.ReadOptionalId(reader, 3),
            OptionsJson = reader.GetString(4),
        };
    }

    /// <inheritdoc />
    public override object?[] Values(StepModelChoice row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return
        [
            row.Step,
            row.Provider,
            row.Model,
            Sql.Id(row.KeyId),
            row.OptionsJson,
        ];
    }
}
