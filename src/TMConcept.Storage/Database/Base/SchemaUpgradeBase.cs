using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;

namespace TMConcept.Storage.Database;

/// <summary>
/// One step of the file's schema history: brings a file of <see cref="From"/> to the next version, inside the caller's transaction, on a
/// connection without foreign key enforcement (a table rebuild drops and renames a table; with foreign keys on, the drop would cascade into
/// the rows that point at it). Schema changes are EF Core migration operations: EF Core writes their SQL for SQLite (a dropped column is a
/// table rebuild from the current model) and data moves through the context, so no SQL is written by hand.
/// </summary>
internal abstract class SchemaUpgradeBase
{
    /// <summary>The version this step upgrades (to <see cref="From"/> + 1).</summary>
    public abstract int From { get; }

    /// <summary>Applies the step; <paramref name="tables"/> are the tables the file has now.</summary>
    public abstract Task ApplyAsync(ConceptDbContext db, IReadOnlySet<string> tables, CancellationToken cancellationToken);

    /// <summary>
    /// Runs <paramref name="operations"/> in the current transaction. The SQL comes from EF Core's SQLite migrations generator with the
    /// current model (a rebuilt table gets the current model's columns). The rebuild's <c>PRAGMA foreign_keys</c> lines run too: inside a
    /// transaction SQLite ignores them, and foreign keys are off for this connection anyway.
    /// </summary>
    protected static async Task RunAsync(ConceptDbContext db, IReadOnlyList<MigrationOperation> operations, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (operations.Count == 0)
        {
            return;
        }

        var connection = db.GetService<IRelationalConnection>();
        foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(operations, CurrentModel(db)))
        {
            await command.ExecuteNonQueryAsync(connection, null, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The operations that create <paramref name="table"/> (with its indexes) exactly as a new file has it.</summary>
    protected static IEnumerable<MigrationOperation> Create(ConceptDbContext db, string table)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.GetService<IMigrationsModelDiffer>().GetDifferences(null, CurrentModel(db).GetRelationalModel())
            .Where(o => o is CreateTableOperation c && c.Name == table || o is CreateIndexOperation i && i.Table == table);
    }

    /// <summary>Whether <paramref name="table"/> has <paramref name="column"/>.</summary>
    protected static Task<bool> HasColumnAsync(ConceptDbContext db, string table, string column, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.TableColumns.AnyAsync(c => c.Table == table && c.Name == column, cancellationToken);
    }

    // The model with its relational mapping (the design-time model: the runtime model leaves out what migrations need).
    private static IModel CurrentModel(ConceptDbContext db) => db.GetService<IDesignTimeModel>().Model;
}
