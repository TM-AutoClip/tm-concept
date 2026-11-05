using Microsoft.Data.Sqlite;

namespace TMConcept.Storage.Database;

/// <summary>
/// One table and its row type, mapped by hand (no reflection, so trimming and native compilation keep it working). A table lists its columns
/// in schema order, reads a row by those positions and gives a row's values in the same order. Statements are built once from the names.
/// </summary>
internal abstract class TableBase<T>
{
    private string? _select;
    private string? _insert;
    private string? _upsert;

    /// <summary>Table name.</summary>
    public abstract string Name { get; }

    /// <summary>Columns in schema order.</summary>
    public abstract IReadOnlyList<string> Columns { get; }

    /// <summary>Primary key columns.</summary>
    public abstract IReadOnlyList<string> Key { get; }

    /// <summary><c>SELECT</c> of every column (append a WHERE).</summary>
    public string Select => _select ??= $"SELECT {string.Join(", ", Columns.Select(Quote))} FROM {Quote(Name)}";

    /// <summary>Reads the current row (columns in <see cref="Columns"/> order).</summary>
    public abstract T Read(SqliteDataReader reader);

    /// <summary>A row's values in <see cref="Columns"/> order, as stored.</summary>
    public abstract object?[] Values(T row);

    /// <summary>Rows matching <paramref name="where"/> (a SQL condition with <c>$name</c> parameters; empty = all), in <paramref name="orderBy"/> order.</summary>
    public async Task<List<T>> ListAsync(SqliteConnection connection, string where, CancellationToken cancellationToken,
        SqliteTransaction? transaction = null, string orderBy = "", params (string Name, object? Value)[] parameters)
    {
        var text = Select + (where.Length > 0 ? " WHERE " + where : string.Empty) + (orderBy.Length > 0 ? " ORDER BY " + orderBy : string.Empty);
        return await QueryAsync(connection, text, cancellationToken, transaction, parameters).ConfigureAwait(false);
    }

    /// <summary>Rows of any query whose columns are this table's columns in order.</summary>
    public async Task<List<T>> QueryAsync(SqliteConnection connection, string text, CancellationToken cancellationToken, SqliteTransaction? transaction = null,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = Sql.Command(connection, text, transaction, parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var list = new List<T>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(Read(reader));
        }

        return list;
    }

    /// <summary>The first row matching <paramref name="where"/>, or default.</summary>
    public async Task<T?> FirstAsync(SqliteConnection connection, string where, CancellationToken cancellationToken, SqliteTransaction? transaction = null,
        params (string Name, object? Value)[] parameters)
    {
        var list = await QueryAsync(connection, Select + " WHERE " + where + " LIMIT 1", cancellationToken, transaction, parameters).ConfigureAwait(false);
        return list.Count == 0 ? default : list[0];
    }

    /// <summary>Adds a row (fails when its key exists).</summary>
    public Task InsertAsync(SqliteConnection connection, T row, CancellationToken cancellationToken, SqliteTransaction? transaction = null) =>
        Sql.ExecuteAsync(connection, _insert ??= $"INSERT INTO {Quote(Name)} ({string.Join(", ", Columns.Select(Quote))}) VALUES ({Parameters()})",
            cancellationToken, transaction, Bind(row));

    /// <summary>Adds a row or replaces every column of the row with its key.</summary>
    public Task UpsertAsync(SqliteConnection connection, T row, CancellationToken cancellationToken, SqliteTransaction? transaction = null) =>
        Sql.ExecuteAsync(connection, _upsert ??= $"INSERT INTO {Quote(Name)} ({string.Join(", ", Columns.Select(Quote))}) VALUES ({Parameters()}) " +
            $"ON CONFLICT ({string.Join(", ", Key.Select(Quote))}) DO UPDATE SET " +
            string.Join(", ", Columns.Where(c => !Key.Contains(c)).Select(c => $"{Quote(c)} = excluded.{Quote(c)}")),
            cancellationToken, transaction, Bind(row));

    /// <summary>Deletes the rows matching <paramref name="where"/>; returns how many.</summary>
    public Task<int> DeleteAsync(SqliteConnection connection, string where, CancellationToken cancellationToken, SqliteTransaction? transaction = null,
        params (string Name, object? Value)[] parameters) =>
        Sql.ExecuteAsync(connection, $"DELETE FROM {Quote(Name)} WHERE {where}", cancellationToken, transaction, parameters);

    /// <summary>How many rows match <paramref name="where"/> (empty = all).</summary>
    public async Task<int> CountAsync(SqliteConnection connection, string where, CancellationToken cancellationToken, params (string Name, object? Value)[] parameters) =>
        (int)(await Sql.ScalarAsync(connection, $"SELECT COUNT(*) FROM {Quote(Name)}" + (where.Length > 0 ? " WHERE " + where : string.Empty),
            cancellationToken, null, parameters).ConfigureAwait(false) ?? 0);

    /// <summary>A quoted name.</summary>
    protected static string Quote(string name) => "\"" + name + "\"";

    private string Parameters() => string.Join(", ", Columns.Select((_, i) => "$c" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    private (string Name, object? Value)[] Bind(T row)
    {
        var values = Values(row);
        if (values.Length != Columns.Count)
        {
            throw new InvalidOperationException($"{Name}: {values.Length} values for {Columns.Count} columns.");
        }

        return values.Select((v, i) => ("$c" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), v)).ToArray();
    }
}
