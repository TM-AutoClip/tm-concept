using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace TMConcept.Tests.Storage;

/// <summary>
/// The table of 0.1.17 whose columns changed since then, as 0.1.17 mapped it, so an upgrade test can write data into a file of that version
/// through EF Core (the app's own mapping already has the newer columns).
/// </summary>
internal sealed class Version0117Context(DbContextOptions<Version0117Context> options) : DbContext(options)
{
    public DbSet<OldSuggestionRow> TopicSuggestions => Set<OldSuggestionRow>();

    /// <summary>Opens <paramref name="connectionString"/>.</summary>
    public static Version0117Context Open(string connectionString) => new(new DbContextOptionsBuilder<Version0117Context>().UseSqlite(connectionString).Options);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OldSuggestionRow>(e =>
        {
            e.ToTable("topic_suggestions");
            e.HasKey(r => r.Id);
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        SqliteConnection.ClearAllPools();
    }
}
