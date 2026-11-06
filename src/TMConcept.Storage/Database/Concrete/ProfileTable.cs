using Microsoft.Data.Sqlite;
using TMConcept.Core.Profiles;

namespace TMConcept.Storage.Database;

/// <summary>The <c>profiles</c> table (<see cref="Profile"/>).</summary>
internal sealed class ProfileTable : TableBase<Profile>
{
    /// <summary>The table.</summary>
    public static ProfileTable Instance { get; } = new();

    /// <inheritdoc />
    public override string Name => "profiles";

    /// <inheritdoc />
    public override IReadOnlyList<string> Columns { get; } = ["Id", "Name", "ContentLanguage", "Format", "TargetSeconds", "ConceptText", "TemplateId", "ConceptCounter", "CreatedAtUtc", "UpdatedAtUtc"];

    /// <inheritdoc />
    public override IReadOnlyList<string> Key { get; } = ["Id"];

    /// <inheritdoc />
    public override Profile Read(SqliteDataReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return new Profile
        {
            Id = Sql.ReadId(reader, 0),
            Name = reader.GetString(1),
            ContentLanguage = reader.GetString(2),
            Format = (VideoFormat)reader.GetInt32(3),
            TargetSeconds = reader.GetInt32(4),
            ConceptText = reader.GetString(5),
            TemplateId = Sql.ReadOptionalText(reader, 6),
            ConceptCounter = reader.GetInt32(7),
            CreatedAtUtc = Sql.ReadTime(reader, 8),
            UpdatedAtUtc = Sql.ReadTime(reader, 9),
        };
    }

    /// <inheritdoc />
    public override object?[] Values(Profile row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return
        [
            Sql.Id(row.Id),
            row.Name,
            row.ContentLanguage,
            (int)row.Format,
            row.TargetSeconds,
            row.ConceptText,
            row.TemplateId,
            row.ConceptCounter,
            Sql.Ticks(row.CreatedAtUtc),
            Sql.Ticks(row.UpdatedAtUtc),
        ];
    }
}
