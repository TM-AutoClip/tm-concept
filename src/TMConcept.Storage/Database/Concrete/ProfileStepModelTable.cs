using Microsoft.Data.Sqlite;
using TMConcept.Core.Models;

namespace TMConcept.Storage.Database;

/// <summary>The <c>profile_step_models</c> table: a profile's step overrides (<see cref="StepModelChoice"/> with a profile id).</summary>
internal sealed class ProfileStepModelTable : TableBase<StepModelChoice>
{
    /// <summary>The table.</summary>
    public static ProfileStepModelTable Instance { get; } = new();

    /// <inheritdoc />
    public override string Name => "profile_step_models";

    /// <inheritdoc />
    public override IReadOnlyList<string> Columns { get; } = ["ProfileId", "Step", "Provider", "Model", "KeyId", "OptionsJson"];

    /// <inheritdoc />
    public override IReadOnlyList<string> Key { get; } = ["ProfileId", "Step"];

    /// <inheritdoc />
    public override StepModelChoice Read(SqliteDataReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return new StepModelChoice
        {
            ProfileId = Sql.ReadId(reader, 0),
            Step = reader.GetString(1),
            Provider = reader.GetString(2),
            Model = reader.GetString(3),
            KeyId = Sql.ReadOptionalId(reader, 4),
            OptionsJson = reader.GetString(5),
        };
    }

    /// <inheritdoc />
    public override object?[] Values(StepModelChoice row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return
        [
            Sql.Id(row.ProfileId ?? throw new InvalidOperationException("A profile step override needs a profile id.")),
            row.Step,
            row.Provider,
            row.Model,
            Sql.Id(row.KeyId),
            row.OptionsJson,
        ];
    }
}
