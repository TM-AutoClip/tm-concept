using Microsoft.Data.Sqlite;
using TMConcept.Core.Videos;

namespace TMConcept.Storage.Database;

/// <summary>The <c>video_costs</c> table (<see cref="VideoCost"/>).</summary>
internal sealed class VideoCostTable : TableBase<VideoCost>
{
    /// <summary>The table.</summary>
    public static VideoCostTable Instance { get; } = new();

    /// <inheritdoc />
    public override string Name => "video_costs";

    /// <inheritdoc />
    public override IReadOnlyList<string> Columns { get; } = ["Id", "VideoId", "Step", "RunId", "Usd", "Exact"];

    /// <inheritdoc />
    public override IReadOnlyList<string> Key { get; } = ["Id"];

    /// <inheritdoc />
    public override VideoCost Read(SqliteDataReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return new VideoCost
        {
            Id = Sql.ReadId(reader, 0),
            VideoId = Sql.ReadId(reader, 1),
            Step = reader.GetString(2),
            RunId = Sql.ReadOptionalId(reader, 3),
            Usd = Sql.ReadMoney(reader, 4),
            Exact = reader.GetInt64(5) != 0,
        };
    }

    /// <inheritdoc />
    public override object?[] Values(VideoCost row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return
        [
            Sql.Id(row.Id),
            Sql.Id(row.VideoId),
            row.Step,
            Sql.Id(row.RunId),
            Sql.Money(row.Usd),
            Sql.Flag(row.Exact),
        ];
    }
}
