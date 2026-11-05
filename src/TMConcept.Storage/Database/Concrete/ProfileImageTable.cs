using Microsoft.Data.Sqlite;
using TMConcept.Core.Profiles;

namespace TMConcept.Storage.Database;

/// <summary>The <c>profile_images</c> table (<see cref="ProfileImage"/>).</summary>
internal sealed class ProfileImageTable : TableBase<ProfileImage>
{
    /// <summary>The table.</summary>
    public static ProfileImageTable Instance { get; } = new();

    /// <inheritdoc />
    public override string Name => "profile_images";

    /// <inheritdoc />
    public override IReadOnlyList<string> Columns { get; } = ["Id", "ProfileId", "FileName", "Caption", "Width", "Height", "Sha256", "AddedAtUtc"];

    /// <inheritdoc />
    public override IReadOnlyList<string> Key { get; } = ["Id"];

    /// <inheritdoc />
    public override ProfileImage Read(SqliteDataReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return new ProfileImage
        {
            Id = Sql.ReadId(reader, 0),
            ProfileId = Sql.ReadId(reader, 1),
            FileName = reader.GetString(2),
            Caption = reader.GetString(3),
            Width = reader.GetInt32(4),
            Height = reader.GetInt32(5),
            Sha256 = reader.GetString(6),
            AddedAtUtc = Sql.ReadTime(reader, 7),
        };
    }

    /// <inheritdoc />
    public override object?[] Values(ProfileImage row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return
        [
            Sql.Id(row.Id),
            Sql.Id(row.ProfileId),
            row.FileName,
            row.Caption,
            row.Width,
            row.Height,
            row.Sha256,
            Sql.Ticks(row.AddedAtUtc),
        ];
    }
}
