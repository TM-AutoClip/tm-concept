using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace TMConcept.Tests.Storage;

/// <summary>
/// The table of 0.1.18 whose columns changed since then, as 0.1.18 mapped it, so an upgrade test can write data into a file of that version
/// through EF Core (the app's own mapping already has the channel photo column).
/// </summary>
internal sealed class Version0118Context(DbContextOptions<Version0118Context> options) : DbContext(options)
{
    public DbSet<OldProfileRow> Profiles => Set<OldProfileRow>();

    /// <summary>Opens <paramref name="connectionString"/>.</summary>
    public static Version0118Context Open(string connectionString) => new(new DbContextOptionsBuilder<Version0118Context>().UseSqlite(connectionString).Options);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OldProfileRow>(e =>
        {
            e.ToTable("profiles");
            e.HasKey(r => r.Id);
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        SqliteConnection.ClearAllPools();
    }
}
