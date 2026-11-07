using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TMConcept.Storage.Database;

namespace TMConcept.Tests.Storage;

/// <summary>
/// A test's look at a database file as it is stored, through EF Core: the raw columns of <c>runs</c> (no value converters) with SQLite's
/// <c>typeof</c>, and the schema SQL in <c>sqlite_master</c>.
/// </summary>
internal sealed class InspectionContext(DbContextOptions<InspectionContext> options) : DbContext(options)
{
    public DbSet<RawRunRow> Runs => Set<RawRunRow>();

    public DbSet<MasterSqlRow> Master => Set<MasterSqlRow>();

    /// <summary>SQLite's <c>typeof</c> of a text column ("text", "blob", "null", …); only inside a query.</summary>
    public static string SqliteType(string? value) => throw new NotSupportedException(value);

    /// <summary>SQLite's <c>typeof</c> of a number column; only inside a query.</summary>
    public static string SqliteType(long? value) => throw new NotSupportedException(value?.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Opens <paramref name="connectionString"/>.</summary>
    public static InspectionContext Open(string connectionString) =>
        new(new DbContextOptionsBuilder<InspectionContext>().UseSqlite(connectionString).UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDbFunction(typeof(InspectionContext).GetMethod(nameof(SqliteType), [typeof(string)])!).HasName("typeof").IsBuiltIn();
        modelBuilder.HasDbFunction(typeof(InspectionContext).GetMethod(nameof(SqliteType), [typeof(long?)])!).HasName("typeof").IsBuiltIn();
        modelBuilder.Entity<RawRunRow>(e =>
        {
            e.ToTable("runs");
            e.HasKey(r => r.Id);
        });
        modelBuilder.Entity<MasterSqlRow>(e =>
        {
            e.HasNoKey().ToView("sqlite_master");
            e.Property(m => m.Type).HasColumnName("type");
            e.Property(m => m.Name).HasColumnName("name");
            e.Property(m => m.Sql).HasColumnName("sql");
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        SqliteConnection.ClearAllPools();
    }
}
