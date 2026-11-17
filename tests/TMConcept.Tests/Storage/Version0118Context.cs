using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace TMConcept.Tests.Storage;

/// <summary>
/// The tables of 0.1.18 whose columns changed since then, as 0.1.18 mapped them, so an upgrade test can write data into a file of that version
/// (or of 0.1.17–0.1.20 for the concepts) through EF Core (the app's own mapping already has the channel photo column of 0.1.19 and the
/// concepts' Sources column of 0.1.21).
/// </summary>
internal sealed class Version0118Context(DbContextOptions<Version0118Context> options) : DbContext(options)
{
    public DbSet<OldProfileRow> Profiles => Set<OldProfileRow>();

    public DbSet<OldConceptRow> Concepts => Set<OldConceptRow>();

    /// <summary>Opens <paramref name="connectionString"/>.</summary>
    public static Version0118Context Open(string connectionString) => new(new DbContextOptionsBuilder<Version0118Context>().UseSqlite(connectionString).Options);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OldProfileRow>(e =>
        {
            e.ToTable("profiles");
            e.HasKey(r => r.Id);
        });
        modelBuilder.Entity<OldConceptRow>(e =>
        {
            e.ToTable("concepts");
            e.HasKey(r => r.Id);
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        SqliteConnection.ClearAllPools();
    }
}
