using Microsoft.Data.Sqlite;
using TMConcept.Core.Videos;

namespace TMConcept.Storage.Database;

/// <summary>The <c>videos</c> table (<see cref="VideoRecord"/>).</summary>
internal sealed class VideoTable : TableBase<VideoRecord>
{
    /// <summary>The table.</summary>
    public static VideoTable Instance { get; } = new();

    /// <inheritdoc />
    public override string Name => "videos";

    /// <inheritdoc />
    public override IReadOnlyList<string> Columns { get; } = ["Id", "ConceptId", "ProfileId", "Title", "Folder", "FileName", "ThumbnailName", "DurationMs", "Width", "Height", "Fps", "SizeBytes", "Engine", "EngineVersion", "CodeModel", "ConceptModel", "SpeechModel", "ImageModel", "SettingsJson", "PromptVersion", "TotalUsd", "CostExact", "ElapsedMs", "Warnings", "CreatedAtUtc"];

    /// <inheritdoc />
    public override IReadOnlyList<string> Key { get; } = ["Id"];

    /// <inheritdoc />
    public override VideoRecord Read(SqliteDataReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return new VideoRecord
        {
            Id = Sql.ReadId(reader, 0),
            ConceptId = Sql.ReadOptionalId(reader, 1),
            ProfileId = Sql.ReadId(reader, 2),
            Title = reader.GetString(3),
            Folder = reader.GetString(4),
            FileName = reader.GetString(5),
            ThumbnailName = reader.GetString(6),
            DurationMs = reader.GetInt64(7),
            Width = reader.GetInt32(8),
            Height = reader.GetInt32(9),
            Fps = reader.GetInt32(10),
            SizeBytes = reader.GetInt64(11),
            Engine = reader.GetString(12),
            EngineVersion = reader.GetString(13),
            CodeModel = reader.GetString(14),
            ConceptModel = reader.GetString(15),
            SpeechModel = reader.GetString(16),
            ImageModel = reader.GetString(17),
            SettingsJson = reader.GetString(18),
            PromptVersion = reader.GetString(19),
            TotalUsd = Sql.ReadMoney(reader, 20),
            CostExact = reader.GetInt64(21) != 0,
            ElapsedMs = reader.GetInt64(22),
            Warnings = reader.GetString(23),
            CreatedAtUtc = Sql.ReadTime(reader, 24),
        };
    }

    /// <inheritdoc />
    public override object?[] Values(VideoRecord row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return
        [
            Sql.Id(row.Id),
            Sql.Id(row.ConceptId),
            Sql.Id(row.ProfileId),
            row.Title,
            row.Folder,
            row.FileName,
            row.ThumbnailName,
            row.DurationMs,
            row.Width,
            row.Height,
            row.Fps,
            row.SizeBytes,
            row.Engine,
            row.EngineVersion,
            row.CodeModel,
            row.ConceptModel,
            row.SpeechModel,
            row.ImageModel,
            row.SettingsJson,
            row.PromptVersion,
            Sql.Money(row.TotalUsd),
            Sql.Flag(row.CostExact),
            row.ElapsedMs,
            row.Warnings,
            Sql.Ticks(row.CreatedAtUtc),
        ];
    }
}
