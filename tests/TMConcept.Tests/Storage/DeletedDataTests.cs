using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TMConcept.Core.Concepts;
using TMConcept.Core.Keys;
using TMConcept.Core.Profiles;
using TMConcept.Storage.Database;

namespace TMConcept.Tests.Storage;

/// <summary>
/// Deleted data leaves nothing readable behind, although 0.1.7 no longer runs VACUUM after a delete (it is SQL, and EF Core has no
/// counterpart; the developer accepted on 7 Nov 2026 that the file is not made smaller): the SQLite build zeroes deleted cells and the
/// pages that become free (secure delete), so the file holds no trace of them. Checked on an unencrypted file of the same SQLite build,
/// with the same EF Core deletes the stores and the backup use, because an encrypted file's pages cannot be read plain without rebuilding
/// the file (a rekey to no key runs VACUUM, which would drop the free pages and prove nothing). Encryption sits below the B-tree and does
/// not change what a delete leaves. Every check first finds the text before the delete, so it is shown to find a trace when there is one.
/// The text fills overflow pages, which go to the free list when the row is deleted.
/// </summary>
public sealed class DeletedDataTests
{
    private const string KeySecret = "deleted-key-secret-7f3a";
    private const string ConceptMarker = "deleted-concept-text-91c4 ";
    private const int ConceptRepeats = 2000;
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task APermanentlyDeletedConceptLeavesNoTraceAndItsPagesStayInTheFileZeroed()
    {
        using var paths = new TempPaths();
        var file = paths.DatabaseFile;
        await FillAsync(file);
        Contains(file, ConceptMarker).ShouldBeTrue("the check finds the text before the delete");
        var before = new FileInfo(file).Length;

        await using (var db = await ConceptDatabase.OpenAsync(Plain(file), None))
        {
            (await db.Concepts.Where(c => c.Title == "t").ExecuteDeleteAsync(None)).ShouldBe(1);
        }

        SqliteConnection.ClearAllPools();
        Contains(file, ConceptMarker).ShouldBeFalse();
        new FileInfo(file).Length.ShouldBe(before, "the file is not rebuilt after a delete: the freed pages stay in it, overwritten with zeros");
    }

    [Fact]
    public async Task WhatIsRemovedFromABackupCopyLeavesNoTraceInIt()
    {
        using var paths = new TempPaths();
        var file = paths.DatabaseFile;
        var copy = Path.Combine(paths.DataDirectory, "copy.db");
        await FillAsync(file);

        // As a backup is made: SQLite's online backup of the whole file, then what was not chosen is deleted from the copy.
        using (var source = new SqliteConnection(Plain(file)))
        using (var destination = new SqliteConnection(Plain(copy)))
        {
            source.Open();
            destination.Open();
            source.BackupDatabase(destination);
        }

        SqliteConnection.ClearAllPools();
        (Contains(copy, KeySecret), Contains(copy, ConceptMarker)).ShouldBe((true, true), "the check finds what the copy holds");
        await using (var db = await ConceptDatabase.OpenAsync(Plain(copy), None))
        {
            await db.ApiKeys.ExecuteDeleteAsync(None);
            await db.Concepts.ExecuteDeleteAsync(None);
        }

        SqliteConnection.ClearAllPools();
        (Contains(copy, KeySecret), Contains(copy, ConceptMarker)).ShouldBe((false, false));
    }

    /// <summary>A current schema with a profile, a concept of <see cref="ConceptRepeats"/> markers and an API key.</summary>
    private static async Task FillAsync(string file)
    {
        await using var db = await ConceptDatabase.OpenAsync(Plain(file), None);
        await ConceptDatabase.EnsureSchemaAsync(db, None);
        (await db.SecureDelete.Select(s => s.Value).SingleAsync(None)).ShouldBe(ConceptSchema.SecureDeleteOn);
        var profile = new Profile { Name = "Channel" };
        db.Profiles.Add(profile);
        db.Concepts.Add(new Concept
        {
            ProfileId = profile.Id,
            Number = 1,
            Title = "t",
            Summary = "s",
            Content = string.Concat(Enumerable.Repeat(ConceptMarker, ConceptRepeats)),
            Language = "en",
        });
        db.ApiKeys.Add(new ApiKey { Provider = "openai", Label = "x", Secret = KeySecret });
        await db.SaveChangesAsync(None);
    }

    /// <summary>An unencrypted file, otherwise as the app opens its database.</summary>
    private static string Plain(string file) => new SqliteConnectionStringBuilder
    {
        DataSource = file,
        Pooling = false,
        ForeignKeys = true,
        Mode = SqliteOpenMode.ReadWriteCreate,
    }.ToString();

    /// <summary>Whether <paramref name="text"/> (UTF-8, as SQLite stores TEXT) is anywhere in <paramref name="file"/>.</summary>
    private static bool Contains(string file, string text) =>
        File.ReadAllBytes(file).AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes(text)) >= 0;
}
