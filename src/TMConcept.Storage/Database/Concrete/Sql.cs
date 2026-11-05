using System.Globalization;
using Microsoft.Data.Sqlite;

namespace TMConcept.Storage.Database;

/// <summary>
/// Value formats of the database file, the same as the 0.1.0–0.1.2 files written by EF Core: ids are upper-case TEXT, times are UTC ticks
/// (INTEGER), money is invariant TEXT ("0.0###…"), flags are 0/1, enums their number. Commands and reads go through here, so every table
/// writes and reads them one way.
/// </summary>
internal static class Sql
{
    private const string MoneyFormat = "0.0###########################";

    /// <summary>A command with named parameters (<c>$name</c>), inside <paramref name="transaction"/> when given.</summary>
    public static SqliteCommand Command(SqliteConnection connection, string text, SqliteTransaction? transaction = null,
        params (string Name, object? Value)[] parameters)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var command = connection.CreateCommand();
        command.CommandText = text;
        command.Transaction = transaction;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }

    /// <summary>Runs a statement; returns the changed row count.</summary>
    public static async Task<int> ExecuteAsync(SqliteConnection connection, string text, CancellationToken cancellationToken, SqliteTransaction? transaction = null,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = Command(connection, text, transaction, parameters);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs a query that returns one number (COUNT, MAX); null when it returns NULL.</summary>
    public static async Task<long?> ScalarAsync(SqliteConnection connection, string text, CancellationToken cancellationToken, SqliteTransaction? transaction = null,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = Command(connection, text, transaction, parameters);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is null or DBNull ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    /// <summary>An <c>IN (…)</c> list for <paramref name="values"/> (parameters <c>$in0</c>, <c>$in1</c>…); empty values match nothing.</summary>
    public static (string Clause, (string Name, object? Value)[] Parameters) In(string column, IEnumerable<object?> values)
    {
        var list = values.ToList();
        if (list.Count == 0)
        {
            return ("0", []);
        }

        var parameters = list.Select((v, i) => ("$in" + i.ToString(CultureInfo.InvariantCulture), v)).ToArray();
        return ($"\"{column}\" IN ({string.Join(", ", parameters.Select(p => p.Item1))})", parameters);
    }

    /// <summary>An id as stored.</summary>
    public static string Id(Guid value) => value.ToString("D").ToUpperInvariant();

    /// <summary>An optional id as stored.</summary>
    public static object? Id(Guid? value) => value is { } id ? Id(id) : null;

    /// <summary>A time as stored (UTC ticks).</summary>
    public static long Ticks(DateTimeOffset value) => value.UtcTicks;

    /// <summary>An optional time as stored.</summary>
    public static object? Ticks(DateTimeOffset? value) => value?.UtcTicks;

    /// <summary>Money as stored.</summary>
    public static string Money(decimal value) => value.ToString(MoneyFormat, CultureInfo.InvariantCulture);

    /// <summary>A flag as stored.</summary>
    public static long Flag(bool value) => value ? 1 : 0;

    /// <summary>Reads an id.</summary>
    public static Guid ReadId(SqliteDataReader reader, int ordinal) => Guid.Parse(reader.GetString(ordinal));

    /// <summary>Reads an optional id.</summary>
    public static Guid? ReadOptionalId(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : ReadId(reader, ordinal);

    /// <summary>Reads a time.</summary>
    public static DateTimeOffset ReadTime(SqliteDataReader reader, int ordinal) => new(reader.GetInt64(ordinal), TimeSpan.Zero);

    /// <summary>Reads an optional time.</summary>
    public static DateTimeOffset? ReadOptionalTime(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : ReadTime(reader, ordinal);

    /// <summary>Reads money.</summary>
    public static decimal ReadMoney(SqliteDataReader reader, int ordinal) =>
        decimal.Parse(reader.GetString(ordinal), NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>Reads an optional text.</summary>
    public static string? ReadOptionalText(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    /// <summary>Reads an optional number.</summary>
    public static double? ReadOptionalReal(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);
}
