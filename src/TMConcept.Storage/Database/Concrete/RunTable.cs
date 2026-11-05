using Microsoft.Data.Sqlite;
using TMConcept.Core.Runs;

namespace TMConcept.Storage.Database;

/// <summary>The <c>runs</c> table (<see cref="Run"/>).</summary>
internal sealed class RunTable : TableBase<Run>
{
    /// <summary>The table.</summary>
    public static RunTable Instance { get; } = new();

    /// <inheritdoc />
    public override string Name => "runs";

    /// <inheritdoc />
    public override IReadOnlyList<string> Columns { get; } = ["Id", "ProfileId", "Kind", "Provider", "Model", "KeyLabel", "Requested", "Accepted", "RejectedDuplicates", "ExtraPrompt", "EstimateMinUsd", "EstimateMaxUsd", "ActualUsd", "CostExact", "InputTokens", "CachedTokens", "CacheWriteTokens", "OutputTokens", "StartedAtUtc", "FinishedAtUtc", "Status", "Error"];

    /// <inheritdoc />
    public override IReadOnlyList<string> Key { get; } = ["Id"];

    /// <inheritdoc />
    public override Run Read(SqliteDataReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return new Run
        {
            Id = Sql.ReadId(reader, 0),
            ProfileId = Sql.ReadOptionalId(reader, 1),
            Kind = (RunKind)reader.GetInt32(2),
            Provider = reader.GetString(3),
            Model = reader.GetString(4),
            KeyLabel = reader.GetString(5),
            Requested = reader.GetInt32(6),
            Accepted = reader.GetInt32(7),
            RejectedDuplicates = reader.GetInt32(8),
            ExtraPrompt = reader.GetString(9),
            EstimateMinUsd = Sql.ReadMoney(reader, 10),
            EstimateMaxUsd = Sql.ReadMoney(reader, 11),
            ActualUsd = Sql.ReadMoney(reader, 12),
            CostExact = reader.GetInt64(13) != 0,
            InputTokens = reader.GetInt64(14),
            CachedTokens = reader.GetInt64(15),
            CacheWriteTokens = reader.GetInt64(16),
            OutputTokens = reader.GetInt64(17),
            StartedAtUtc = Sql.ReadTime(reader, 18),
            FinishedAtUtc = Sql.ReadOptionalTime(reader, 19),
            Status = (RunStatus)reader.GetInt32(20),
            Error = reader.GetString(21),
        };
    }

    /// <inheritdoc />
    public override object?[] Values(Run row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return
        [
            Sql.Id(row.Id),
            Sql.Id(row.ProfileId),
            (int)row.Kind,
            row.Provider,
            row.Model,
            row.KeyLabel,
            row.Requested,
            row.Accepted,
            row.RejectedDuplicates,
            row.ExtraPrompt,
            Sql.Money(row.EstimateMinUsd),
            Sql.Money(row.EstimateMaxUsd),
            Sql.Money(row.ActualUsd),
            Sql.Flag(row.CostExact),
            row.InputTokens,
            row.CachedTokens,
            row.CacheWriteTokens,
            row.OutputTokens,
            Sql.Ticks(row.StartedAtUtc),
            Sql.Ticks(row.FinishedAtUtc),
            (int)row.Status,
            row.Error,
        ];
    }
}
