using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TMConcept.Storage.Database;

namespace TMConcept.Tests.Storage;

/// <summary>
/// A test's look at a database file as it is stored, through EF Core: the raw columns of <c>runs</c> (no value converters) with SQLite's
/// <c>typeof</c>, the schema SQL in <c>sqlite_master</c>, and every table's columns, foreign keys and indexes (SQLite's pragma views,
/// read through their hidden <c>arg</c> column).
/// </summary>
internal sealed class InspectionContext(DbContextOptions<InspectionContext> options) : DbContext(options)
{
    public DbSet<RawRunRow> Runs => Set<RawRunRow>();

    public DbSet<MasterSqlRow> Master => Set<MasterSqlRow>();

    public DbSet<TableColumnRow> Columns => Set<TableColumnRow>();

    public DbSet<ForeignKeyRow> ForeignKeys => Set<ForeignKeyRow>();

    public DbSet<IndexRow> Indexes => Set<IndexRow>();

    /// <summary>SQLite's <c>typeof</c> of a text column ("text", "blob", "null", …); only inside a query.</summary>
    public static string SqliteType(string? value) => throw new NotSupportedException(value);

    /// <summary>SQLite's <c>typeof</c> of a number column; only inside a query.</summary>
    public static string SqliteType(long? value) => throw new NotSupportedException(value?.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Opens <paramref name="connectionString"/>.</summary>
    public static InspectionContext Open(string connectionString) =>
        new(new DbContextOptionsBuilder<InspectionContext>().UseSqlite(connectionString).UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options);

    /// <summary>Every table with its columns (name, type, not null, key), foreign keys and indexes — in a comparable form.</summary>
    public async Task<List<string>> SchemaAsync()
    {
        var lines = new List<string>();
        var tables = await Master.Where(m => m.Type == "table" && !m.Name.StartsWith("sqlite_")).OrderBy(m => m.Name).Select(m => m.Name).ToListAsync();
        foreach (var table in tables)
        {
            lines.AddRange((await Columns.Where(c => c.Table == table).OrderBy(c => c.Position).ToListAsync())
                .Select(c => $"{table} column {c.Name}|{c.Type}|{c.NotNull}|{c.Key}"));
            lines.AddRange((await ForeignKeys.Where(f => f.Table == table).ToListAsync()).OrderBy(f => f.From, StringComparer.Ordinal)
                .Select(f => $"{table} fk {f.References}|{f.From}|{f.To}|{f.OnDelete}"));
            lines.AddRange((await Indexes.Where(i => i.Table == table && i.Origin == "c").ToListAsync()).OrderBy(i => i.Name, StringComparer.Ordinal)
                .Select(i => $"{table} index {i.Name}|{i.Unique}"));
        }

        return lines;
    }

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
        modelBuilder.Entity<TableColumnRow>(e =>
        {
            e.HasNoKey().ToView("pragma_table_info");
            e.Property(c => c.Table).HasColumnName("arg");
            e.Property(c => c.Position).HasColumnName("cid");
            e.Property(c => c.Name).HasColumnName("name");
            e.Property(c => c.Type).HasColumnName("type");
            e.Property(c => c.NotNull).HasColumnName("notnull");
            e.Property(c => c.Key).HasColumnName("pk");
        });
        modelBuilder.Entity<ForeignKeyRow>(e =>
        {
            e.HasNoKey().ToView("pragma_foreign_key_list");
            e.Property(f => f.Table).HasColumnName("arg");
            e.Property(f => f.References).HasColumnName("table");
            e.Property(f => f.From).HasColumnName("from");
            e.Property(f => f.To).HasColumnName("to");
            e.Property(f => f.OnDelete).HasColumnName("on_delete");
        });
        modelBuilder.Entity<IndexRow>(e =>
        {
            e.HasNoKey().ToView("pragma_index_list");
            e.Property(i => i.Table).HasColumnName("arg");
            e.Property(i => i.Name).HasColumnName("name");
            e.Property(i => i.Unique).HasColumnName("unique");
            e.Property(i => i.Origin).HasColumnName("origin");
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        SqliteConnection.ClearAllPools();
    }
}
